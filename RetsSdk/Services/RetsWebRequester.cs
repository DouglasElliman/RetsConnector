﻿﻿using Microsoft.Extensions.Logging;
using CrestApps.RetsSdk.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using CrestApps.RetsSdk.Helpers;
using Microsoft.Extensions.Options;

namespace CrestApps.RetsSdk.Services
{
    public class RetsWebRequester : IRetsRequester
    {
        private readonly ConnectionOptions Options;
        private readonly IHttpClientFactory HttpClientFactory;

        public RetsWebRequester(IOptions<ConnectionOptions> options, IHttpClientFactory httpClientFactory)
        {
            Options = options.Value;
            HttpClientFactory = httpClientFactory;
        }


        public async Task Get(Uri uri, Action<HttpResponseMessage> action, bool backEnd, SessionResource resource = null, bool ensureSuccessStatusCode = true)
        {
            using (var client = GetClient(resource, backEnd))
            {
                var response = await client.GetAsync(uri);

                if (uri.ToString().EndsWith("/logout"))
                {
                    //Console.WriteLine(await response.Content.ReadAsStringAsync());    
                }
                
                if (ensureSuccessStatusCode)
                {
                    response.EnsureSuccessStatusCode();
                }

                action?.Invoke(response);
            }
        }

        public async Task<T> Get<T>(Uri uri, Func<HttpResponseMessage, Task<T>> action, bool backEnd, SessionResource resource = null, bool ensureSuccessStatusCode = true) where T : class
        {
            using (var client = GetClient(resource, backEnd))
            {
                #region Keep
               //  var request = new HttpRequestMessage(HttpMethod.Get, "https://r_idx.gsmls.com/rets_idx/login.do");
               //  request.Headers.Add("RETS-UA-Authorization", "Digest cf6487fbe11e6c35b4197cc239618bae");
               //  request.Headers.Add("User-Agent", "DouglasEllimanofNJ/1.0");
               //  request.Headers.Add("RETS-Version", "RETS/1.5");
               //  var response1 = await client.SendAsync(request);
               //
               // var x =  request.Headers.Authorization.ToString();
               //Authorization: Digest username="DouglasEllimanofNJ",realm="r_idx.gsmls.com",nonce="168bad92c945eda7b667e80af3093aee",uri="/rets_idx/login.do",cnonce="262bc259d7fe8fecfccd15767c29900d",nc=00000001,response="e6de9adf65af3ff5b4af1b1b17dc19eb",qop="auth",opaque="5ccdef346870ab04ddfe0412367fccba"
               
               
                //Console.WriteLine(await response1.Content.ReadAsStringAsync());
                // var request = new HttpRequestMessage(HttpMethod.Get, "https://r_idx.gsmls.com/rets_idx/login.do");
                // request.Headers.Add("RETS-UA-Authorization", "Digest cf6487fbe11e6c35b4197cc239618bae");
                // request.Headers.Add("User-Agent", "DouglasEllimanofNJ/1.0");
                // request.Headers.Add("RETS-Version", "RETS/1.5");
                // request.Headers.Add("Cookie", "JSESSIONID=W_kX9uAvRNysMktwzutEvhM8zI4XRXpOeGCwwf0q.jbvend5; RETS-Session-ID=W_kX9uAvRNysMktwzutEvhM8zI4XRXpOeGCwwf0q");
                // var response1 = await client.SendAsync(request);
                //

                //foreach (var header in response1.Headers)
                //{
                //    Console.WriteLine(header.Key + ": " + header.Value.ToString());
                //}
                //var wwwAuthenticateHeaderValue = response1.Headers.GetValues("WWW-Authenticate").FirstOrDefault();
                #endregion
                using (var response = await client.GetAsync(uri))
                {
                    if (ensureSuccessStatusCode)
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    // Buffer the body as raw bytes. Reading it into a string and re-encoding it as UTF-8
                    // destroys binary payloads (images), because every byte that is not valid UTF-8 gets
                    // replaced with U+FFFD.
                    byte[] content = await response.Content.ReadAsByteArrayAsync();

                    var newResponse = new HttpResponseMessage(response.StatusCode)
                    {
                        Content = new ByteArrayContent(content),
                        ReasonPhrase = response.ReasonPhrase,
                        Version = response.Version
                    };

                    foreach (var header in response.Headers)
                    {
                        newResponse.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    // Content headers must be carried over too. Content-Type in particular holds the
                    // multipart boundary, without which the response cannot be split into its parts.
                    // Content-Length is skipped so ByteArrayContent can derive it from the buffer.
                    foreach (var header in response.Content.Headers)
                    {
                        if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        newResponse.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    return await action?.Invoke(newResponse);
                }
            }
        }


        public async Task Get(Uri uri, bool backEnd, SessionResource resource = null, bool ensureSuccessStatusCode = true)
        {
            await Get(uri, null, backEnd, resource, ensureSuccessStatusCode);
        }

        protected virtual HttpClient GetClient(SessionResource resource, bool backEnd)
        {
            HttpClient client = GetAuthenticatedClient(backEnd);

            client.Timeout = new TimeSpan(0, 20, 0);
            
            if (Options.UserAgentPassword != "")
            {
                var agent = Str.Md5($"{Options.UserAgent}:{Options.UserAgentPassword}");
                var test = Str.Md5($"{agent}:::{Options.Version.AsHeader()}");
                //Console.WriteLine(test);
                client.DefaultRequestHeaders.Add("RETS-UA-Authorization", $"Digest {test}");
            }

            
            //var agentdata1 = Str.Md5($"{Str.Md5($"{Options.UserAgent}:{Options.UserAgentPassword}")}:{Options.Version.AsHeader()}");
            
            
            client.Timeout = Options.Timeout;
            client.DefaultRequestHeaders.Add("User-Agent", Options.UserAgent);
            client.DefaultRequestHeaders.Add("RETS-Version", Options.Version.AsHeader());
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
            client.DefaultRequestHeaders.Add("Accept", "*/*");
            //client.DefaultRequestHeaders.Add("RETS-UA-Authorization", "Digest cf6487fbe11e6c35b4197cc239618bae");
            
            if (resource != null && !string.IsNullOrWhiteSpace(resource.Cookie))
            {
                //client.DefaultRequestHeaders.Add("Set-Cookie", resource.Cookie);
                client.DefaultRequestHeaders.Add("Cookie", resource.Cookie);
            }

            if (resource != null && !string.IsNullOrWhiteSpace(resource.SessionId))
            {
                client.DefaultRequestHeaders.Add("RETS-Session-ID", resource.SessionId);
            }

            return client;
        }

        private HttpClient GetAuthenticatedClient(bool backEnd)
        {
            if (Options.Type == Models.Enums.AuthenticationType.Digest)
            {
                // Reuse a pooled SocketsHttpHandler across all Digest-auth requests so we don't burn a
                // fresh TCP+TLS connection per call. Creating a new HttpClientHandler + HttpClient per
                // request (previous behavior) exhausts SNAT ports in cloud environments like Azure
                // Container Apps / App Service and manifests as HTTP 200 responses with empty bodies.
                var handler = GetSharedDigestHandler(backEnd);
                return new HttpClient(handler, disposeHandler: false);
            }

            HttpClient client = HttpClientFactory.CreateClient();

            byte[] byteArray;
            if (backEnd)
            {
                byteArray= Encoding.ASCII.GetBytes($"{Options.PrivateUsername}:{Options.PrivatePassword}");
            }
            else
            {
                byteArray= Encoding.ASCII.GetBytes($"{Options.PublicUsername}:{Options.PublicPassword}");    
            }
            
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            return client;
        }

        // Shared, connection-pooled handlers keyed by credential scope (public vs private/backEnd).
        // These are static so a single set of pooled connections is reused across every RetsWebRequester
        // instance for the lifetime of the process.
        private static SocketsHttpHandler? _sharedDigestHandlerPublic;
        private static SocketsHttpHandler? _sharedDigestHandlerPrivate;
        private static readonly object _sharedDigestHandlerLock = new object();

        private SocketsHttpHandler GetSharedDigestHandler(bool backEnd)
        {
            var existing = backEnd ? _sharedDigestHandlerPrivate : _sharedDigestHandlerPublic;
            if (existing != null)
            {
                return existing;
            }

            lock (_sharedDigestHandlerLock)
            {
                existing = backEnd ? _sharedDigestHandlerPrivate : _sharedDigestHandlerPublic;
                if (existing != null)
                {
                    return existing;
                }

                var credCache = new CredentialCache();
                var username = backEnd ? Options.PrivateUsername : Options.PublicUsername;
                var password = backEnd ? Options.PrivatePassword : Options.PublicPassword;
                credCache.Add(new Uri(Options.LoginUrl), Options.Type.ToString(), new NetworkCredential(username, password));

                var handler = new SocketsHttpHandler
                {
                    Credentials = credCache,
                    // UseCookies=false preserves prior behavior where Cookie/RETS-Session-ID headers are
                    // added explicitly per request in GetClient(...). See original comment referencing
                    // https://stackoverflow.com/a/13287224 for the net48 vs modern-.NET rationale.
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                    MaxConnectionsPerServer = 4,
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, sslPolicyErrors) =>
                        {
                            // Bypass SSL hostname mismatch for GSMLS RETS server.
                            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch)
                            {
                                return true;
                            }
                            return sslPolicyErrors == SslPolicyErrors.None;
                        }
                    }
                };

                if (backEnd)
                {
                    _sharedDigestHandlerPrivate = handler;
                }
                else
                {
                    _sharedDigestHandlerPublic = handler;
                }
                return handler;
            }
        }
    }
}
