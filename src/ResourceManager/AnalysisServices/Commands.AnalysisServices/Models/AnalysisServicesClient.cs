﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using Microsoft.Azure.Commands.AnalysisServices.Properties;
using Microsoft.Azure.Commands.Common.Authentication;
using Microsoft.Azure.Commands.Common.Authentication.Abstractions;
using Microsoft.Azure.Commands.ResourceManager.Common.Tags;
using Microsoft.Azure.Management.Analysis;
using Microsoft.Azure.Management.Analysis.Models;
using Microsoft.Rest.Azure;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Microsoft.Azure.Commands.AnalysisServices.Models
{
    public class AnalysisServicesClient
    {
        private readonly AnalysisServicesManagementClient _client;
        private readonly Guid _subscriptionId;
        private readonly string _currentUser;

        public AnalysisServicesClient(IAzureContext context)
        {
            if (context == null)
            {
                throw new ApplicationException(Resources.InvalidDefaultSubscription);
            }

            _subscriptionId = context.Subscription.GetId();
            _client = AzureSession.Instance.ClientFactory.CreateArmClient<AnalysisServicesManagementClient>(
                context, 
                AzureEnvironment.Endpoint.ResourceManager);
            _currentUser = context.Account.Id;
        }

        public AnalysisServicesClient()
        {
        }

        #region Server Related Operations

        public AnalysisServicesServer CreateOrUpdateServer(
            string resourceGroupName, 
            string serverName,
            string location, 
            string skuName = null,
            Hashtable customTags = null,
            string administrators = null,
            AnalysisServicesServer existingServer = null,
            string backupBlobContainerUri = null,
            string gatewayName = null)
        {
            if (string.IsNullOrEmpty(resourceGroupName))
            {
                resourceGroupName = GetResourceGroupByServer(serverName);
            }

            var tags = (customTags != null)
                ? TagsConversionHelper.CreateTagDictionary(customTags, true)
                : null;

            var adminList = new List<string>();

            if (!string.IsNullOrEmpty(administrators))
            {
                adminList.AddRange(administrators.Split(','));
                if (adminList.Count == 0)
                {
                    adminList.Add(_currentUser);
                }
            }

            GatewayDetails gatewayDetails = null;
			if (gatewayName == "-")
			{
				_client.Servers.DissociateGateway(resourceGroupName, serverName);
			}
			else if (!string.IsNullOrEmpty(gatewayName))
            {
                gatewayDetails = getConnectionGateway(gatewayName);
            }

            AnalysisServicesServer newOrUpdatedServer = null;
            if (existingServer != null)
            {
                var updateParameters = new AnalysisServicesServerUpdateParameters()
                {
                    Sku = skuName == null ? existingServer.Sku : GetResourceSkuFromName(skuName),
                    Tags = tags,
                };

                if (adminList.Count > 0)
                {
                    updateParameters.AsAdministrators = new ServerAdministrators(adminList);
                }

                if (backupBlobContainerUri != null)
                {
                    updateParameters.BackupBlobContainerUri = backupBlobContainerUri;
                }

                if (gatewayDetails != null)
                {
                    updateParameters.GatewayDetails = gatewayDetails;
                }

                newOrUpdatedServer = _client.Servers.Update(resourceGroupName, serverName, updateParameters);				
			}
            else
            {
                newOrUpdatedServer = _client.Servers.Create(
                    resourceGroupName, 
                    serverName, 
                    new AnalysisServicesServer()
                    {
                        AsAdministrators = new ServerAdministrators(adminList),
                        BackupBlobContainerUri = backupBlobContainerUri,
                        GatewayDetails = gatewayDetails,
                        Location = location,
                        Sku = GetResourceSkuFromName(skuName),
                        Tags = tags
                    });
            }

            return newOrUpdatedServer;
        }

        public void DeleteServer(string resourceGroupName, string serverName)
        {
            if (string.IsNullOrEmpty(resourceGroupName))
            {
                resourceGroupName = GetResourceGroupByServer(serverName);
            }

            _client.Servers.Delete(resourceGroupName, serverName);
        }

        public bool TestServer(string resourceGroupName, string serverName, out AnalysisServicesServer server)
        {
            try
            {
                server = GetServer(resourceGroupName, serverName);
                return true;
            }
            catch (CloudException ex)
            {
                if ((ex.Response != null && ex.Response.StatusCode == HttpStatusCode.NotFound) || ex.Message.Contains(string.Format(Properties.Resources.FailedToDiscoverResourceGroup, serverName,
                    _subscriptionId)))
                {
                    server = null;
                    return false;
                }

                throw;
            }
        }

        public AnalysisServicesServer GetServer(string resourceGroupName, string serverName)
        {
            if (string.IsNullOrEmpty(resourceGroupName))
            {
                resourceGroupName = GetResourceGroupByServer(serverName);
            }

            return _client.Servers.GetDetails(resourceGroupName, serverName);
        }

        public List<AnalysisServicesServer> ListServers(string resourceGroupName)
        {
            var serverList = new List<AnalysisServicesServer>();
            var response = string.IsNullOrEmpty(resourceGroupName) ?
                _client.Servers.List() :
                _client.Servers.ListByResourceGroup(resourceGroupName);

            serverList.AddRange(response);

            return serverList;
        }

        public SkuEnumerationForNewResourceResult ListSkusForNew()
        {
            return _client.Servers.ListSkusForNew();
        }

        public SkuEnumerationForExistingResourceResult ListSkusForExisting(string resourceGroupName, string serverName)
        {
            if (string.IsNullOrEmpty(resourceGroupName))
            {
                resourceGroupName = GetResourceGroupByServer(serverName);
            }

            return _client.Servers.ListSkusForExisting(resourceGroupName, serverName);
        }

        public GatewayListStatusLive GetGatewayStatus(string resourceGroupName, string serverName)
        {
            return _client.Servers.ListGatewayStatus(resourceGroupName, serverName);
        }

        private string GetResourceGroupByServer(string serverName)
        {
            try
            {
                var acctId =
                    ListServers(null)
                        .Find(x => x.Name.Equals(serverName, StringComparison.InvariantCultureIgnoreCase))
                        .Id;
                var rgStart = acctId.IndexOf("resourceGroups/", StringComparison.InvariantCultureIgnoreCase) +
                              ("resourceGroups/".Length);
                var rgLength = (acctId.IndexOf("/providers/", StringComparison.InvariantCultureIgnoreCase)) - rgStart;
                return acctId.Substring(rgStart, rgLength);
            }
            catch
            {
                throw new CloudException(string.Format(Properties.Resources.FailedToDiscoverResourceGroup, serverName, _subscriptionId));
            }
        }

        private ResourceSku GetResourceSkuFromName(string skuName)
        {
            var tier = skuName.StartsWith("D") ? SkuTier.Development
                : skuName.StartsWith("B") ? SkuTier.Basic
                : SkuTier.Standard;
            return new ResourceSku(skuName, tier);
        }

        public void SuspendServer(string resourceGroupName, string serverName)
        {
            if (string.IsNullOrEmpty(resourceGroupName))
            {
                resourceGroupName = GetResourceGroupByServer(serverName);
            }

            _client.Servers.Suspend(resourceGroupName, serverName);
        }

        public void ResumeServer(string resourceGroupName, string serverName)
        {
            if (string.IsNullOrEmpty(resourceGroupName))
            {
                resourceGroupName = GetResourceGroupByServer(serverName);
            }

            _client.Servers.Resume(resourceGroupName, serverName);
        }

		public GatewayInfo GetGatewayInfo(string resourceId)
		{
			var availableGateways = getAvailableGateways();
			foreach (var gateway in availableGateways.value)
			{
				if (gateway.id.Equals(resourceId))
				{
					return gateway;
				}
			}

			return null;
		}


		private const string WebResourceProviderVersion = "api-version=2016-06-01";

        private GatewayDetails getConnectionGateway(string Name)
        {
            var availableGateways = getAvailableGateways();
            GatewayDetails targetGateway = null;
            foreach( var gateway in availableGateways.value)
            {
                if (gateway.name.Equals(Name))
                {
                    targetGateway = new GatewayDetails(gateway.id);
                }
            }

            return targetGateway;
        }

        private GatewayCollection getAvailableGateways()
        {
            var _baseUrl = _client.BaseUri.AbsoluteUri;
            var _url = new Uri(new Uri(_baseUrl + (_baseUrl.EndsWith("/") ? "" : "/")), "subscriptions/{subscriptionId}/providers/Microsoft.Web/connectionGateways").ToString();
            _url = _url.Replace("{subscriptionId}", Uri.EscapeDataString(_client.SubscriptionId));
            _url += "?" + WebResourceProviderVersion;
            var _httpRequest = new HttpRequestMessage();
            HttpResponseMessage _httpResponse = null;
            _httpRequest.Method = new HttpMethod("GET");
            _httpRequest.RequestUri = new Uri(_url);
            _httpRequest.Headers.TryAddWithoutValidation("x-ms-client-request-id", System.Guid.NewGuid().ToString());
            _httpRequest.Headers.TryAddWithoutValidation("accept-language", _client.AcceptLanguage);
            _client.Credentials.ProcessHttpRequestAsync(_httpRequest, new CancellationToken()).ConfigureAwait(false).GetAwaiter();
            _httpResponse =_client.HttpClient.SendAsync(_httpRequest).ConfigureAwait(false).GetAwaiter().GetResult();
            var jsonstring = _httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            GatewayCollection availableGateways = JsonConvert.DeserializeObject<GatewayCollection>(jsonstring);
            return availableGateways;
        }

        #endregion
    }
}