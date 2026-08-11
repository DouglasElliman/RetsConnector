﻿using Microsoft.Extensions.Logging;
using MimeKit;
using MimeTypes.Core;
using CrestApps.RetsSdk.Contracts;
using CrestApps.RetsSdk.Exceptions;
using CrestApps.RetsSdk.Helpers.Extensions;
using CrestApps.RetsSdk.Models;
using CrestApps.RetsSdk.Models.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace CrestApps.RetsSdk.Services
{
    public class RetsClient : RetsResponseBase<RetsClient>, IRetsClient
    {
        private readonly IRetsRequester Requester;
        private readonly IRetsSession Session;
        private bool BackEnd { get; set; }
        protected Uri GetObjectUri => Session.Resource.GetCapability(Capability.GetObject);
        protected Uri SearchUri => Session.Resource.GetCapability(Capability.Search);
        protected Uri GetMetadataUri => Session.Resource.GetCapability(Capability.GetMetadata);

        public RetsClient(IRetsSession session, IRetsRequester requester, ILogger<RetsClient> logger)
            : base(logger)
        {
            Session = session ?? throw new ArgumentNullException($"{nameof(session)} cannot be null");
            Requester = requester ?? throw new ArgumentNullException($"{nameof(requester)} cannot be null");
        }

        public bool IsConnected => Session.IsStarted();

        public async Task<bool> Connect(bool backEnd)
        {
            BackEnd = backEnd;
            if (Session.IsStarted()) 
                return true;

            return await Session.Start(BackEnd);
        }

        public async Task Disconnect()
        {
            try
            {
                await Session.End();
            }
            catch (Exception e)
            {
                // ignored
            }
        }

        public async Task<SearchResult> Search(SearchRequest request)
        {
            if (request == null)
            {
                throw new Exception($"{request} cannot be null");
            }

            RetsResource resource = await GetResourceMetadata(request.SearchType);

            if (resource == null)
            {
                var message = $"The provided '{nameof(SearchRequest.SearchType)}' is not valid. You can get a list of all valid value by calling '{nameof(GetResourcesMetadata)}' method on the Session object.";
                throw new Exception(message);
            }

            var uriBuilder = new UriBuilder(SearchUri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query.Add("SearchType", request.SearchType);
            query.Add("Class", request.Class);
            query.Add("QueryType", request.QueryType);
            query.Add("Format", request.Format);
//            query.Add("COUNT_PARAMETER", "1");
            query.Add("Count", ((int) request.Count).ToString());
            query.Add("Limit", request.Limit.ToString());
            query.Add("Offset", request.Offset.ToString());
            
            query.Add("StandardNames", request.StandardNames.ToString());
            query.Add("RestrictedIndicator", request.RestrictedIndicator);
            query.Add("Query", request.RawQuery ?? request.ParameterGroup.ToString());

            if (request.HasColumns())
            {
                var columns = request.GetColumns().ToList();

                if (!request.HasColumn(resource.KeyField))
                {
                    columns.Add(resource.KeyField);
                }

                query.Add("Select", string.Join(",", columns));
            }

            uriBuilder.Query = query.ToString();

            return await Requester.Get(uriBuilder.Uri, async (response) =>
            {
                using (var stream = await GetStream(response))
                {
                    XDocument doc = default;
                        
                    try
                    {
                        using var sr = new StreamReader(stream);
                        var content = await sr.ReadToEndAsync();
                        doc = XDocument.Parse(content);
                        //doc = XDocument.Load(stream);
                    }
                    catch (Exception e)
                    {
                        throw;
                    } 
                        

                    int code = GetReplayCode(doc.Root);

                    AssertValidReplay(doc.Root, code);

                    var result = new SearchResult(resource, request.Class, request.RestrictedIndicator);

                    if (code != 0) 
                        return result;

                    char delimiterValue = GetCompactDelimiter(doc);

                    XNamespace ns = doc.Root.GetDefaultNamespace();
                    XElement columns = doc.Descendants(ns + "COLUMNS").FirstOrDefault();

                    var records = doc.Descendants(ns + "DATA");

                    if (columns != null)
                    {
                        string[] tableColumns = columns.Value.Split(delimiterValue);
                        result.SetColumns(tableColumns);

                        foreach (var record in records)
                        {
                            string[] fields = record.Value.Split(delimiterValue);
                            SearchResultRow row = new SearchResultRow(tableColumns, fields, resource.KeyField, request.RestrictedIndicator);
                            result.AddRow(row);
                        }
                    }

                    var maxRows = doc.Descendants(ns + "MAXROWS").ToArray();
                    result.HasMoreRows = maxRows.Length > 0; // <MAXROWS />

                    var count = doc.Descendants(ns + "COUNT").ToArray();
                    result.ServerCount = count.Length == 0 ? (int?) null : int.Parse(count[0].Attribute("Records").Value); //<COUNT Records="137854" />
                    
                    return result;
                }
            }, BackEnd, Session.Resource);
        }

        private  string CleanInput(string strIn)
        {
            // Replace invalid characters with empty strings.
            try {
                return Regex.Replace(strIn, @"[^\w\.@-]", "",
                    RegexOptions.None, TimeSpan.FromSeconds(1.5));
            }
            // If we timeout when replacing invalid characters,
            // we should return Empty.
            catch (RegexMatchTimeoutException) {
                return String.Empty;
            }
        }
        
        public async Task<RetsSystem> GetSystemMetadata()
        {
            var uriBuilder = new UriBuilder(GetMetadataUri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query.Add("Type", "METADATA-SYSTEM");
            query.Add("ID", "*");
            query.Add("Format", "STANDARD-XML");

            uriBuilder.Query = query.ToString();

            return await Requester.Get(uriBuilder.Uri, async (response) =>
            {
                using (Stream stream = await GetStream(response))
                {
                    XDocument doc = XDocument.Load(stream);

                    AssertValidReplay(doc.Root);

                    XNamespace ns = doc.Root.GetDefaultNamespace();

                    XElement metaData = doc.Descendants(ns + "METADATA").FirstOrDefault();

                    XElement metadataSystem = metaData.Elements().FirstOrDefault();
                    XElement systemMeta = metadataSystem.Elements().FirstOrDefault();

                    XElement metaDataResource = metadataSystem.Descendants("METADATA-RESOURCE").FirstOrDefault();

                    RetsResourceCollection resources = new RetsResourceCollection();
                    resources.Load(metaDataResource);

                    var system = new RetsSystem()
                    {
                        SystemId = systemMeta.Attribute("SystemID")?.Value,
                        SystemDescription = systemMeta.Attribute("SystemDescription")?.Value,

                        Version = metadataSystem.Attribute("Version")?.Value,
                        Date = DateTime.Parse(metadataSystem.Attribute("Date")?.Value),
                        Resources = resources
                    };

                    return system;
                }
            }, BackEnd, Session.Resource);
        }

        public async Task<RetsResourceCollection> GetResourcesMetadata()
        {
            RetsResourceCollection capsule = await MakeMetadataRequest<RetsResourceCollection>("METADATA-RESOURCE", "0");

            return capsule;
        }

        public async Task<RetsResource> GetResourceMetadata(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentNullException($"{resourceId} cannot be null.");
            }

            RetsResourceCollection capsule = await GetResourcesMetadata();

            var resource = capsule.Get().FirstOrDefault(x => x.ResourceId.Equals(resourceId, StringComparison.CurrentCultureIgnoreCase)) ?? throw new ResourceDoesNotExists();

            return resource;
        }

        public async Task<RetsClassCollection> GetClassesMetadata(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentNullException($"{resourceId} cannot be null.");
            }

            return await MakeMetadataRequest<RetsClassCollection>("METADATA-CLASS", resourceId);
        }

        public async Task<RetsObjectCollection> GetObjectMetadata(string resourceId)
        {
            return await MakeMetadataRequest<RetsObjectCollection>("METADATA-OBJECT", resourceId);
        }


        public async Task<RetsLookupTypeCollection> GetLookupValues(string resourceId, string lookupName)
        {
            return await MakeMetadataRequest<RetsLookupTypeCollection>("METADATA-LOOKUP_TYPE", string.Format("{0}:{1}", resourceId, lookupName));
        }

        public async Task<IEnumerable<RetsLookupTypeCollection>> GetLookupValues(string resourceId)
        {
            return await MakeMetadataCollectionRequest<RetsLookupTypeCollection>("METADATA-LOOKUP_TYPE", resourceId);
        }

        public async Task<RetsFieldCollection> GetTableMetadata(string resourceId, string className)
        {
            return await MakeMetadataRequest<RetsFieldCollection>("METADATA-TABLE", string.Format("{0}:{1}", resourceId, className));
        }

        public async Task<IEnumerable<FileObject>> GetObject(string resource, string type, PhotoId id, bool useLocation = false)
        {
            return await GetObject(resource, type, new List<PhotoId> { id }, useLocation);
        }


        public async Task<IEnumerable<FileObject>> GetObject(string resource, string type, IEnumerable<PhotoId> ids, int batchSize, bool useLocation = false)
        {
            if (string.IsNullOrWhiteSpace(resource))
            {
                throw new ArgumentNullException($"{nameof(resource)} cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentNullException($"{nameof(type)} cannot be null.");
            }

            if (ids == null)
            {
                throw new ArgumentNullException($"{nameof(ids)} cannot be null.");
            }

            List<FileObject> files = new List<FileObject>();

            IEnumerable<IEnumerable<PhotoId>> pages = ids.Partition(batchSize);

            foreach (var page in pages)
            {
                // To prevent having to many outstanding requests
                // we should connect, force round trip on every page 

                IEnumerable<FileObject> _files = await RoundTrip(async () =>
                {
                    return await GetObject(resource, type, page, useLocation);
                });

                files.AddRange(_files);
            }

            return files;
        }



        public async Task RoundTrip(Func<Task> action)
        {
            try
            {
                if (!Session.IsStarted())
                {
                    await Connect(BackEnd);
                }

                action?.Invoke();

            }
            catch
            {
                throw;
            }
            finally
            {
                await Disconnect();
            }
        }


        public async Task<TResult> RoundTrip<TResult>(Func<Task<TResult>> action)
        {
            try
            {
                if (!Session.IsStarted())
                {
                    await Connect(BackEnd);
                }

                TResult result = await action.Invoke();
                return result;
            }
            catch
            {
                throw;
            }
            finally
            {
                await Disconnect();
            }
        }



        // RETS reply codes that indicate a temporary server-side condition and are worth retrying.
        // The 204xx codes are the spec's GetObject range:
        //   20408 - Resource unavailable, 20409 - Unavailable, 20411 - Timeout.
        // The 205xx codes are the spec's GetMetadata range, but many servers use them as a generic error
        // range for every transaction, so a GetObject call can come back with the 205xx equivalent:
        //   20508 - Resource unavailable, 20509 - Unavailable, 20511 - Timeout.
        // (20412/20512 "too many outstanding requests" are surfaced separately as TooManyOutstandingRequests.)
        // The miscellaneous error codes are deliberately absent, see NoObjectReplyCodes below.
        private static readonly int[] TransientObjectReplyCodes =
        {
            20408, 20409, 20411,
            20508, 20509, 20511
        };

        /// <summary>
        /// Reply codes that mean "this id has no objects of the requested type", which is a normal
        /// outcome rather than a failure. 20403 is the spec's "No Object Found", but many servers answer
        /// a photo-less listing with the miscellaneous error code instead: 20413, or its 205xx alias 20513
        /// on servers that use the GetMetadata range as a generic error range.
        /// Override this to tighten the list if your server reports misc errors accurately.
        /// </summary>
        protected virtual IReadOnlyCollection<int> NoObjectReplyCodes { get; } = new[] { 20403, 20413, 20513 };

        /// <summary>
        /// Total number of attempts made for a GetObject request before a transient failure is surfaced.
        /// </summary>
        protected virtual int MaxObjectAttempts => 3;

        /// <summary>
        /// Base delay used for the exponential backoff between GetObject retries.
        /// </summary>
        protected virtual TimeSpan ObjectRetryBaseDelay => TimeSpan.FromSeconds(1);

        public async Task<IEnumerable<FileObject>> GetObject(string resource, string type, IEnumerable<PhotoId> ids, bool useLocation = false)
        {
            if (string.IsNullOrWhiteSpace(resource))
            {
                throw new ArgumentNullException($"{nameof(resource)} cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentNullException($"{nameof(type)} cannot be null.");
            }

            if (ids == null)
            {
                throw new ArgumentNullException($"{nameof(ids)} cannot be null.");
            }

            // Materialize so the ids are not re-enumerated on every retry attempt.
            IList<PhotoId> idList = ids as IList<PhotoId> ?? ids.ToList();
            string idText = string.Join(",", idList.Select(x => x.ToString()));

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await RequestObject(resource, type, idList, useLocation);
                }
                catch (RetsException ex) when (attempt < MaxObjectAttempts
                    && ex.ReplyCode.HasValue
                    && Array.IndexOf(TransientObjectReplyCodes, ex.ReplyCode.Value) >= 0)
                {
                    // Exponential backoff with jitter so concurrent workers don't retry in lockstep.
                    double backoffMs = ObjectRetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
                    TimeSpan delay = TimeSpan.FromMilliseconds(backoffMs + Random.Shared.Next(0, 250));

                    Log?.LogWarning("GetObject for {Resource}/{Type} ID={Ids} failed with transient ReplyCode {ReplyCode} on attempt {Attempt} of {MaxAttempts}. Retrying in {DelayMs}ms. {Message}",
                        resource, type, idText, ex.ReplyCode, attempt, MaxObjectAttempts, (int)delay.TotalMilliseconds, ex.Message);

                    await Task.Delay(delay);
                }
                catch (RetsException ex) when (ex.ReplyCode.HasValue
                    && Array.IndexOf(TransientObjectReplyCodes, ex.ReplyCode.Value) >= 0)
                {
                    // Every attempt returned the same code, so the condition is very likely not transient
                    // for this particular request. Surface the ids so the listing can be investigated.
                    Log?.LogError("GetObject for {Resource}/{Type} ID={Ids} still failing with ReplyCode {ReplyCode} after {MaxAttempts} attempts. {Message}",
                        resource, type, idText, ex.ReplyCode, MaxObjectAttempts, ex.Message);

                    throw;
                }
            }
        }

        private async Task<IEnumerable<FileObject>> RequestObject(string resource, string type, IEnumerable<PhotoId> ids, bool useLocation)
        {
            var uriBuilder = new UriBuilder(GetObjectUri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query.Add("Resource", resource);
            query.Add("Type", type);
            query.Add("ID", string.Join(",", ids.Select(x => x.ToString())));
            query.Add("Location", useLocation ? "1" : "0");

            uriBuilder.Query = query.ToString();

            return await Requester.Get(uriBuilder.Uri, async (response) =>
            {
                string responseContentType = GetRawContentType(response);

                var files = new List<FileObject>();

                if (!ContentType.TryParse(responseContentType, out ContentType documentContentType))
                {
                    return files;
                }

                using (Stream memoryStream = await GetStream(response))
                {
                    if (documentContentType.MediaSubtype.Equals("xml", StringComparison.CurrentCultureIgnoreCase))
                    {
                        // An XML body means the server returned a status document rather than the objects.
                        XDocument doc = XDocument.Load(memoryStream);

                        int replyCode = GetReplayCode(doc.Root);

                        if (NoObjectReplyCodes.Contains(replyCode))
                        {
                            // The listing simply has no objects of this type. That is a normal outcome,
                            // not an error, so return an empty collection instead of throwing.
                            Log?.LogDebug("GetObject for {Resource}/{Type} ID={Ids} returned ReplyCode {ReplyCode}; treating as no objects available.",
                                resource, type, string.Join(",", ids.Select(x => x.ToString())), replyCode);

                            return files;
                        }

                        AssertValidReplay(doc.Root, replyCode);

                        return files;
                    }

                    Stream bodyStream = memoryStream;

                    if (documentContentType.MediaType.Equals("multipart", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrEmpty(documentContentType.Boundary))
                    {
                        // Some RETS servers omit the boundary parameter, or send it unquoted with characters
                        // that make it unparsable. Buffer the body and recover the boundary from the payload.
                        var buffered = new MemoryStream();
                        await memoryStream.CopyToAsync(buffered);
                        buffered.Position = 0;
                        bodyStream = buffered;

                        string boundary = SniffMultipartBoundary(buffered);

                        if (string.IsNullOrEmpty(boundary))
                        {
                            Log.LogWarning("Received a '{ContentType}' response with no usable multipart boundary. Unable to extract any objects.", responseContentType);

                            return files;
                        }

                        Log.LogWarning("The '{ContentType}' response header did not carry a usable boundary. Recovered '{Boundary}' from the response body.", responseContentType, boundary);

                        documentContentType.Boundary = boundary;
                    }

                    MimeEntity entity = MimeEntity.Load(documentContentType, bodyStream);

                    if (entity is Multipart multipart)
                    {
                        // At this point we know this is a multi-image response

                        foreach (MimePart part in multipart.OfType<MimePart>())
                        {
                            files.Add(ProcessMessage(part));
                        }

                        return files;
                    }

                    if (entity is MimePart message)
                    {
                        if (!message.Headers.Contains("Object-ID") && response.Headers.TryGetValues("Object-ID", out var objectIds))
                        {
                            message.Headers.Add("Object-ID", objectIds.FirstOrDefault());
                        }
                        if (!message.Headers.Contains("Content-Description") && response.Headers.TryGetValues("Content-Description", out var contentDescriptions))
                        {
                            message.Headers.Add("Content-Description", contentDescriptions.FirstOrDefault());
                        }
                        if (!message.Headers.Contains("Content-Sub-Description") && response.Headers.TryGetValues("Content-Sub-Description", out var contentSubDescriptions))
                        {
                            message.Headers.Add("Content-Sub-Description", contentSubDescriptions.FirstOrDefault());
                        }
                        if (!message.Headers.Contains("MIME-Version") && response.Headers.TryGetValues("MIME-Version", out var mimeVersions))
                        {
                            message.Headers.Add("MIME-Version", mimeVersions.FirstOrDefault());
                        }
                        if (!message.Headers.Contains("Preferred") && response.Headers.TryGetValues("Preferred", out var preferreds))
                        {
                            message.Headers.Add("Preferred", preferreds.FirstOrDefault());
                        }
                        if (!message.Headers.Contains("Location") && response.Headers.TryGetValues("Location", out var locations))
                        {
                            message.Headers.Add("Location", locations.FirstOrDefault());
                        }

                        if (message.ContentId == null && response.Headers.TryGetValues("Content-Id", out var contentIds))
                        {
                            message.ContentId = contentIds.FirstOrDefault();
                        }
                        if (message.ContentLocation == null && response.Headers.TryGetValues("Content-Location", out var contentLocations))
                        {
                            message.ContentLocation = new Uri(contentLocations.FirstOrDefault());
                        }

                        // At this point we know this is a single image response
                        files.Add(ProcessMessage(message));
                    }
                }

                return files;

            }, BackEnd, Session.Resource);
        }


        protected async Task<T> MakeMetadataRequest<T>(string type, string id, string format = "STANDARD-XML")
            where T : class, IRetsCollectionXElementLoader
        {
            var uriBuilder = new UriBuilder(GetMetadataUri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query.Add("Type", type);
            query.Add("ID", id);
            query.Add("Format", format);

            uriBuilder.Query = query.ToString();

            return await Requester.Get(uriBuilder.Uri, async (response) => await ParseMetadata<T>(response), BackEnd, Session.Resource);
        }


        protected async Task<IEnumerable<T>> MakeMetadataCollectionRequest<T>(string type, string resourceId, string format = "STANDARD-XML")
            where T : class, IRetsCollectionXElementLoader
        {
            var uriBuilder = new UriBuilder(GetMetadataUri);

            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query.Add("Type", type);
            query.Add("ID", $"{resourceId}:*");
            query.Add("Format", format);

            uriBuilder.Query = query.ToString();

            return await Requester.Get(uriBuilder.Uri, async (response) => await ParseMetadataCollection<T>(response), BackEnd, Session.Resource);
        }

        protected async Task<T> ParseMetadata<T>(HttpResponseMessage response)
            where T : class, IRetsCollectionXElementLoader
        {
            using (Stream stream = await GetStream(response))
            {
                XDocument doc;
                try
                {
                    doc = XDocument.Load(stream);
                }
                catch (System.Xml.XmlException)
                {
                    // Log the response details
                    stream.Position = 0;
                    using (var reader = new StreamReader(stream))
                    {
                        var content = await reader.ReadToEndAsync();
                        Console.WriteLine($"[RETS ERROR] XML Parse Error. Status: {response.StatusCode}");
                        Console.WriteLine($"[RETS ERROR] Content Length: {content?.Length ?? 0}");
                        Console.WriteLine($"[RETS ERROR] Content: {content}");
                    }
                    throw;
                }

                AssertValidReplay(doc.Root);

                XNamespace ns = doc.Root.GetDefaultNamespace();

                T collection = (T)Activator.CreateInstance(typeof(T));

                XElement metaData = doc.Descendants(ns + "METADATA").FirstOrDefault();

                if (metaData != null)
                {
                    // INSTEAD OF FirstOrDefault
                    // loop over all the elements
                    XElement metaDataNode = metaData.Elements().FirstOrDefault();
                    if (metaDataNode != null)
                    {
                        collection.Load(metaDataNode);
                    }
                }

                return collection;
            }
        }


        protected async Task<IEnumerable<T>> ParseMetadataCollection<T>(HttpResponseMessage response)
            where T : class, IRetsCollectionXElementLoader
        {
            using (Stream stream = await GetStream(response))
            {
                XDocument doc = XDocument.Load(stream);

                AssertValidReplay(doc.Root);

                XNamespace ns = doc.Root.GetDefaultNamespace();

                var list = new List<T>();

                XElement metaData = doc.Descendants(ns + "METADATA").FirstOrDefault();

                if (metaData != null)
                {
                    // INSTEAD OF FirstOrDefault
                    // loop over all the elements
                    foreach (XElement metaDataNode in metaData.Elements())
                    {
                        T collection = (T)Activator.CreateInstance(typeof(T));

                        collection.Load(metaDataNode);

                        list.Add(collection);
                    }
                }

                return list;
            }
        }


        protected char GetCompactDelimiter(XDocument doc)
        {
            XNamespace ns = doc.Root.GetDefaultNamespace();
            XElement delimiter = doc.Descendants(ns + "DELIMITER").FirstOrDefault();

            if (delimiter == null)
            {
                throw new RetsParsingException("Unable to find the delimiter! Only 'COMPACT' or 'COMPACT-DECODED' are supported when querying the data");
            }

            var delimiterAttribute = delimiter.Attribute("value");

            if (delimiterAttribute != null && int.TryParse(delimiterAttribute.Value, out int value))
            {
                return Convert.ToChar(value);
            }

            return Convert.ToChar(9);
        }

        /// <summary>
        /// Returns the unparsed Content-Type header. <see cref="HttpContentHeaders.ContentType"/> is re-serialized
        /// by the framework, which can silently drop parameters such as an unquoted multipart boundary.
        /// </summary>
        private static string GetRawContentType(HttpResponseMessage response)
        {
            if (response.Content.Headers.NonValidated.TryGetValues("Content-Type", out var rawValues))
            {
                string rawValue = rawValues.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (rawValue != null)
                {
                    return rawValue;
                }
            }

            return response.Content.Headers.ContentType?.ToString();
        }

        /// <summary>
        /// Reads the opening multipart delimiter from the start of the payload and returns the boundary it declares.
        /// The stream position is restored before returning.
        /// </summary>
        private static string SniffMultipartBoundary(Stream stream)
        {
            long origin = stream.Position;

            try
            {
                var buffer = new byte[8192];
                int read = stream.Read(buffer, 0, buffer.Length);
                int lineStart = 0;

                for (int i = 0; i < read; i++)
                {
                    if (buffer[i] != (byte)'\n')
                    {
                        continue;
                    }

                    int lineEnd = i;

                    if (lineEnd > lineStart && buffer[lineEnd - 1] == (byte)'\r')
                    {
                        lineEnd--;
                    }

                    string line = Encoding.ASCII.GetString(buffer, lineStart, lineEnd - lineStart);
                    lineStart = i + 1;

                    if (line.Length <= 2 || !line.StartsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string candidate = line.Substring(2).TrimEnd();

                    // "--boundary--" is the closing delimiter, so it cannot be the first one we find in a valid body.
                    if (candidate.Length == 0 || candidate.EndsWith("--", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return candidate;
                }

                return null;
            }
            finally
            {
                stream.Position = origin;
            }
        }

        protected FileObject ProcessMessage(MimePart message)
        {
            var file = new FileObject()
            {
                ContentId = message.ContentId,
                ContentType = new System.Net.Mime.ContentType(message.ContentType.MimeType),
                ContentDescription = message.Headers["Content-Description"],
                ContentSubDescription = message.Headers["Content-Sub-Description"],
                ContentLocation = message.ContentLocation ?? (message.Headers["Location"] != null ? new Uri(message.Headers["Location"]) : null),
                MimeVersion = message.Headers["MIME-Version"],
                Extension = MimeTypeMap.GetExtension(message.ContentType.MimeType)
            };

            if (int.TryParse(message.Headers["Object-ID"], out int objectId))
            {
                file.ObjectId = objectId;
            }
            else
            {
                // This should never happen
                throw new RetsParsingException("For some reason Object-ID does not exists in the response or it is not an integer value as expected");
            }

            if (bool.TryParse(message.Headers["Preferred"], out bool isPreferred))
            {
                file.IsPreferred = isPreferred;
            }

            if (file.ContentLocation == null)
            {
                file.Content = new MemoryStream();
                message.Content.DecodeTo(file.Content);
                file.Content.Position = 0; // This is important otherwise the next seek with start at the end
            }

            return file;
        }


    }
}
