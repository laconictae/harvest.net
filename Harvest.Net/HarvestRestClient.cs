﻿using Harvest.Net.Serialization;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Harvest.Net.Models;

namespace Harvest.Net
{
    public partial class HarvestRestClient
    {
        /// <summary>
        /// Base URL of API (defaults to https://subdomain.harvestapp.com/)
        /// </summary>
        public string BaseUrl { get; private set; }

        private string Username { get; set; }
        private string Password { get; set; }

        private string ClientId { get; set; }
        private string ClientSecret { get; set; }
        private string AccessToken { get; set; }

        private RestClient _client;

        /// <summary>
        /// Initializes a new client using basic HTTP authentication
        /// </summary>
        /// <param name="subdomain">The subdomain of the harvest account to connect to</param>
        /// <param name="username">The usernamen to authenticate with</param>
        /// <param name="password">The password to athenticate with</param>
        public HarvestRestClient(string subdomain, string username, string password)
        {
            BaseUrl = "https://" + subdomain + ".harvestapp.com/";
            Username = username;
            Password = password;

            var assembly = Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = new AssemblyName(assembly.FullName);
            var version = assemblyName.Version;

            _client = new RestClient();
            _client.UserAgent = "harvest.net/" + version + " (.NET " + Environment.Version.ToString() + ")";
            _client.Authenticator = new HttpBasicAuthenticator(username, password);

            _client.BaseUrl = BaseUrl;
            
            // Harvest API is inconsistent in JSON responses so we'll stick to XML
            _client.ClearHandlers();
            _client.AddHandler("application/xml", new HarvestXmlDeserializer());
            _client.AddHandler("text/xml", new HarvestXmlDeserializer());            
        }

        public HarvestRestClient(string subdomain, string clientId, string clientSecret, string accessToken)
        {
            BaseUrl = "https://" + subdomain + ".harvestapp.com/";

            ClientId = clientId;
            ClientSecret = clientSecret;
            AccessToken = accessToken;

            var assembly = Assembly.GetExecutingAssembly();
            AssemblyName assemblyName = new AssemblyName(assembly.FullName);
            var version = assemblyName.Version;

            _client = new RestClient();
            _client.UserAgent = "harvest.net/" + version + " (.NET " + Environment.Version.ToString() + ")";
            _client.BaseUrl = BaseUrl;
            _client.AddDefaultParameter("access_token", accessToken, ParameterType.UrlSegment);

            _client.ClearHandlers();
            _client.AddHandler("application/xml", new HarvestXmlDeserializer());
            _client.AddHandler("text/xml", new HarvestXmlDeserializer());   
        }

        /// <summary>
        /// Execute a manual REST request
        /// </summary>
        /// <typeparam name="T">The type to create and return</typeparam>
        /// <param name="request">The request to send</param>
        public virtual T Execute<T>(RestRequest request) where T : new()
        {
            var response = _client.Execute<T>(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Created || response.StatusCode == System.Net.HttpStatusCode.Accepted
                || (response.StatusCode == System.Net.HttpStatusCode.OK && request.Method == Method.PUT))
            {
                string location = null;

                var header = response.Headers.FirstOrDefault(h => h.Type == ParameterType.HttpHeader && h.Name == "Location");
                if (header != null)
                {
                    location = (string)header.Value;
                }
                else
                {
                    // Location header is missing try to GET from the original resource instead
                    location = request.Resource;
                }

                var loadRequest = Request(location);
                response = _client.Execute<T>(loadRequest);
            }

            return response.Data;
        }

        public virtual IRestResponse Execute(RestRequest request)
        {
            return _client.Execute(request);
        }

        public OAuth RefreshToken(string refreshToken)
        {
            RestRequest r = new RestRequest();
            r.Method = Method.POST;

            r.Resource = "oauth2/token";
            r.AddParameter("refresh_token", refreshToken);
            r.AddParameter("client_id", ClientId);
            r.AddParameter("client_secret", ClientSecret);
            r.AddParameter("grant_type", "refresh_token");
            r.AddHeader("Content-Type", "x-www-form-urlencoded");

            return this.Execute<OAuth>(r);
        }

        /// <summary>
        /// Initializes a new RestRequest object with a custom XmlSerializer and DateFormat to match Harvest's conventions.
        /// </summary>
        /// <param name="resource">Harvest resource request will hit</param>
        /// <param name="method">HTTP method to use</param>
        protected RestRequest Request(string resource, Method method = Method.GET)
        {
            var request = new RestRequest();
            request.Resource = resource;

            if (AccessToken != null)
                request.Resource = resource + "?access_token=" + AccessToken;

            request.Method = method;

            request.RequestFormat = DataFormat.Xml;
            request.XmlSerializer = new HarvestXmlSerializer();
         
            request.OnBeforeDeserialization = resp =>
            {
                //remove the first ByteOrderMark
                //see: http://stackoverflow.com/questions/19663100/restsharp-has-problems-deserializing-xml-including-byte-order-mark
                string byteOrderMarkUtf8 = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
                if (resp.Content.StartsWith(byteOrderMarkUtf8))
                    resp.Content = resp.Content.TrimStart(byteOrderMarkUtf8.ToArray());
            };

            return request;
        }
    }
}
