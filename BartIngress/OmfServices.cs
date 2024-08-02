using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using OSIsoft.Identity;
using OSIsoft.Omf;

namespace BartIngress
{
    /// <summary>
    /// Manages sending OMF data to the Cds, EDS, and/or PI Web API OMF endpoints
    /// </summary>
    public class OmfServices : IDisposable
    {
        private OmfMessage _typeDeleteMessage;
        private OmfMessage _containerDeleteMessage;

        private AuthenticationHandler CdsAuthenticationHandler { get; set; }
        private HttpClient CdsHttpClient { get; set; }
        private HttpClient EdsHttpClient { get; set; }
        private HttpClientHandler PiHttpClientHandler { get; set; }
        private HttpClient PiHttpClient { get; set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Configure Cds OMF Ingress Service
        /// </summary>
        /// <param name="CdsUri">CONNECT data services OMF Endpoint URI</param>
        /// <param name="tenantId">CONNECT data services Tenant ID</param>
        /// <param name="namespaceId">CONNECT data services Namespace ID</param>
        /// <param name="clientId">CONNECT data services Client ID</param>
        /// <param name="clientSecret">CONNECT data services Client Secret</param>
        internal void ConfigureCdsOmfIngress(Uri CdsUri, string tenantId, string namespaceId, string clientId, string clientSecret)
        {
            CdsAuthenticationHandler = new AuthenticationHandler(CdsUri, clientId, clientSecret)
            {
                InnerHandler = new HttpClientHandler(),
            };

            CdsHttpClient = new HttpClient(CdsAuthenticationHandler)
            {
                BaseAddress = new Uri(CdsUri.AbsoluteUri + $"api/v1/tenants/{tenantId}/namespaces/{namespaceId}/omf"),
            };
        }

        /// <summary>
        /// Configure EDS OMF Ingress Service
        /// </summary>
        /// <param name="port">Edge Data Store Port, default is 5590</param>
        internal void ConfigureEdsOmfIngress(int port = 5590)
        {
            EdsHttpClient = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}/api/v1/tenants/default/namespaces/default/omf"),
            };
        }

        /// <summary>
        /// Configure PI OMF HttpClient
        /// </summary>
        /// <param name="piUri">PI Web API Endpoint URI, like https://server//piwebapi</param>
        /// <param name="username">Domain user name to use for Basic authentication against PI Web API</param>
        /// <param name="password">Domain user password to use for Basic authentication against PI Web API</param>
        /// <param name="validate">Whether to validate the PI Web API endpoint certificate. Setting to false should only be done for testing with a self-signed PI Web API certificate as it is insecure.</param>
        internal void ConfigurePiOmfIngress(Uri piUri, string username, string password, bool validate = true)
        {
            if (validate)
            {
                PiHttpClient = new HttpClient();
            }
            else
            {
                Console.WriteLine("Warning: You have disabled validation of destination certificates. This should only be done for testing with a self-signed PI Web API certificate as it is insecure.");

                PiHttpClientHandler = new HttpClientHandler
                {
                    // This turns off SSL verification
                    // This should not be done in production, please properly handle your certificates
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                };
                PiHttpClient = new HttpClient(PiHttpClientHandler);
            }

            PiHttpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
            PiHttpClient.BaseAddress = new Uri(piUri.AbsoluteUri + $"/omf");
            byte[] byteArray = Encoding.ASCII.GetBytes($"{username}:{password}");
            PiHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        /// <summary>
        /// Sends OMF type message for a type
        /// </summary>
        /// <param name="type">OMF type to be sent</param>
        internal void SendOmfType(Type type)
        {
            OmfTypeMessage msg = OmfMessageCreator.CreateTypeMessage(type);
            SendOmfMessage(msg);
            msg.ActionType = ActionType.Delete;
            _typeDeleteMessage = msg;
        }

        /// <summary>
        /// Sends OMF container messages for a dictionary of OMF data keyed by the stream ID to the configured OMF endpoints
        /// </summary>
        /// <typeparam name="T">OMF type of the OMF data to be sent</typeparam>
        /// <param name="data">Dictionary of OMF data keyed by the stream ID</param>
        /// <param name="typeId">TypeID of the OMF type</param>
        internal void SendOmfContainersForData<T>(Dictionary<string, T> data, string typeId)
        {
            List<OmfContainer> containers = new ();
            foreach (string streamId in data.Keys)
            {
                containers.Add(new OmfContainer(streamId, typeId));
            }

            OmfContainerMessage msg = new (containers);
            SendOmfMessage(msg);
            msg.ActionType = ActionType.Delete;
            _containerDeleteMessage = msg;
        }

        /// <summary>
        /// Sends OMF data messages for a dictionary of OMF data keyed by the stream ID to the configured OMF endpoints
        /// </summary>
        /// <typeparam name="T">OMF type of the OMF data to be sent</typeparam>
        /// <param name="data">Dictionary of OMF data keyed by the stream ID</param>
        internal void SendOmfData<T>(Dictionary<string, IEnumerable<T>> data)
        {
            SendOmfMessage(OmfMessageCreator.CreateDataMessage(data));
        }

        /// <summary>
        /// Sends a message to the configured OMF endpoints
        /// </summary>
        /// <param name="omfMessage">The OMF message to send</param>
        internal void SendOmfMessage(OmfMessage omfMessage)
        {
            SerializedOmfMessage serializedOmfMessage = OmfMessageSerializer.Serialize(omfMessage);

            if (CdsHttpClient != null)
            {
                _ = SendOmfMessageAsync(serializedOmfMessage, CdsHttpClient).Result;
            }

            if (EdsHttpClient != null)
            {
                _ = SendOmfMessageAsync(serializedOmfMessage, EdsHttpClient).Result;
            }

            if (PiHttpClient != null)
            {
                _ = SendOmfMessageAsync(serializedOmfMessage, PiHttpClient).Result;
            }
        }

        /// <summary>
        /// Deletes type and containers that were created by these services
        /// </summary>
        internal void CleanupOmf()
        {
            SerializedOmfMessage serializedTypeDelete = OmfMessageSerializer.Serialize(_typeDeleteMessage);
            SerializedOmfMessage serializedContainerDelete = OmfMessageSerializer.Serialize(_containerDeleteMessage);

            if (CdsHttpClient != null)
            {
                _ = SendOmfMessageAsync(serializedContainerDelete, CdsHttpClient).Result;
                _ = SendOmfMessageAsync(serializedTypeDelete, CdsHttpClient).Result;
            }

            if (EdsHttpClient != null)
            {
                _ = SendOmfMessageAsync(serializedContainerDelete, EdsHttpClient).Result;
                _ = SendOmfMessageAsync(serializedTypeDelete, EdsHttpClient).Result;
            }

            if (PiHttpClient != null)
            {
                _ = SendOmfMessageAsync(serializedContainerDelete, PiHttpClient).Result;
                _ = SendOmfMessageAsync(serializedTypeDelete, PiHttpClient).Result;
            }
        }

        protected virtual void Dispose(bool includeManaged)
        {
            if (includeManaged)
            {
                if (CdsAuthenticationHandler != null)
                {
                    CdsAuthenticationHandler.Dispose();
                }

                if (CdsHttpClient != null)
                {
                    CdsHttpClient.Dispose();
                }
            }
        }

        /// <summary>
        /// Sends an OMF message to an OMF endpoint with optional authentication header
        /// </summary>
        /// <param name="omfMessage">The OMF message to send</param>
        /// <param name="httpClient">HttpClient for the OMF endpoint to send to</param>
        /// <returns>A task returning the response of the HTTP request</returns>
        private static async Task<string> SendOmfMessageAsync(SerializedOmfMessage omfMessage, HttpClient httpClient)
        {
            using HttpRequestMessage request = new ()
            {
                Method = HttpMethod.Post,
                Content = new ByteArrayContent(omfMessage.BodyBytes),
            };

            foreach (OmfHeader omfHeader in omfMessage.Headers)
            {
                request.Headers.Add(omfHeader.Name, omfHeader.Value);
            }

            HttpResponseMessage response = await httpClient.SendAsync(request).ConfigureAwait(false);
            string responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Error sending OMF to endpoint at {httpClient.BaseAddress}. Response code: {response.StatusCode} Response: {responseString}");
            return responseString;
        }
    }
}
