using Microsoft.Extensions.Logging;
using CrestApps.RetsSdk.Models;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
                var response = await client.GetAsync(uri);

                if (uri.ToString().EndsWith("/logout"))
                {
                    //Console.WriteLine(await response.Content.ReadAsStringAsync());    
                }
                
                
                if (ensureSuccessStatusCode)
                {
                    response.EnsureSuccessStatusCode();
                }

                return await action?.Invoke(response);
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
                var credCache = new CredentialCache();
                if (backEnd)
                {
                    credCache.Add(new Uri(Options.LoginUrl), Options.Type.ToString(), new NetworkCredential(Options.PrivateUsername, Options.PrivatePassword));
                }
                else
                {
                    credCache.Add(new Uri(Options.LoginUrl), Options.Type.ToString(), new NetworkCredential(Options.PublicUsername, Options.PublicPassword));    
                }
                

                // The UseCookies and DefaultRequestHeaders.Add("Cookie", ...) have different behavior in net48 and net6.
                // We need to force UseCookies = false to both have the same expect behavior
                // See: https://stackoverflow.com/a/13287224

                return new HttpClient(new HttpClientHandler { Credentials = credCache, UseCookies = false });
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
    }
}
