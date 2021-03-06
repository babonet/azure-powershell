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

using Microsoft.Azure.Commands.Common.Authentication;
using Microsoft.Azure.Gallery;
using Microsoft.Azure.Management.Authorization;
using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Insights;
using Microsoft.Azure.Management.Network;
using Microsoft.Azure.Management.Redis;
using Microsoft.Azure.Management.Resources;
using Microsoft.Azure.Management.Storage;
using Microsoft.Azure.Subscriptions;
using Microsoft.Azure.Test;
using Microsoft.Azure.Test.HttpRecorder;
using Microsoft.WindowsAzure.Commands.ScenarioTest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RestTestFramework = Microsoft.Rest.ClientRuntime.Azure.TestFramework;

namespace Commands.Network.Test
{
    public sealed class NetworkResourcesController
    {
        private CSMTestEnvironmentFactory csmTestFactory;
        private EnvironmentSetupHelper helper;

        public ResourceManagementClient ResourceManagementClient { get; private set; }

        public SubscriptionClient SubscriptionClient { get; private set; }

        public GalleryClient GalleryClient { get; private set; }

        public AuthorizationManagementClient AuthorizationManagementClient { get; private set; }

        public NetworkManagementClient NetworkManagementClient { get; private set; }

        public ComputeManagementClient ComputeManagementClient { get; private set; }

        public StorageManagementClient StorageManagementClient { get; private set; }

        public InsightsManagementClient InsightsManagementClient { get; private set; }

        public RedisManagementClient RedisManagementClient { get; private set; }

        public static NetworkResourcesController NewInstance
        {
            get
            {
                return new NetworkResourcesController();
            }
        }

        public NetworkResourcesController()
        {
            helper = new EnvironmentSetupHelper();
        }

        public void RunPsTest(params string[] scripts)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d.Add("Microsoft.Resources", null);
            d.Add("Microsoft.Compute", null);
            d.Add("Microsoft.Features", null);
            d.Add("Microsoft.Authorization", null);
            d.Add("Microsoft.Storage", null);
            var providersToIgnore = new Dictionary<string, string>();
            providersToIgnore.Add("Microsoft.Azure.Management.Resources.ResourceManagementClient", "2016-02-01");
            providersToIgnore.Add("Microsoft.Azure.Management.Network.NetworkManagementClient", "2017-09-01");
            HttpMockServer.Matcher = new PermissiveRecordMatcherWithApiExclusion(true, d, providersToIgnore);

            var callingClassType = TestUtilities.GetCallingClass(2);
            var mockName = TestUtilities.GetCurrentMethodName(2);

            RunPsTestWorkflow(
                () => scripts,
                // no custom initializer
                null,
                // no custom cleanup 
                null,
                callingClassType,
                mockName);
        }

        public void RunPsTestWorkflow(
            Func<string[]> scriptBuilder,
            Action<CSMTestEnvironmentFactory> initialize,
            Action cleanup,
            string callingClassType,
            string mockName)
        {
            HttpMockServer.RecordsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SessionRecords");
            using (RestTestFramework.MockContext context = RestTestFramework.MockContext.Start(callingClassType, mockName))
            {
                this.csmTestFactory = new CSMTestEnvironmentFactory();

                if (initialize != null)
                {
                    initialize(this.csmTestFactory);
                }

                SetupManagementClients(context);

                helper.SetupEnvironment(AzureModule.AzureResourceManager);

                var networkPath = helper.GetStackRMModulePath("AzureRM.Network");
                var computePath= helper.GetStackRMModulePath("AzureRM.Compute");

                var callingClassName = callingClassType
                                        .Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries)
                                        .Last();
                helper.SetupModules(AzureModule.AzureResourceManager,
                    "ScenarioTests\\Common.ps1",
                    "ScenarioTests\\" + callingClassName + ".ps1",
                    helper.StackRMProfileModule,
                    helper.StackRMResourceModule,
                    helper.GetRMModulePath("AzureRM.Insights.psd1"),
                    helper.GetRMModulePath("AzureRM.RedisCache.psd1"),
                    networkPath,
                    computePath,
                    helper.RMStorageDataPlaneModule,
                    helper.StackRMStorageModule);

                try
                {
                    if (scriptBuilder != null)
                    {
                        var psScripts = scriptBuilder();

                        if (psScripts != null)
                        {
                            helper.RunPowerShellTest(psScripts);
                        }
                    }
                }
                finally
                {
                    if (cleanup != null)
                    {
                        cleanup();
                    }
                }
            }
        }

        private Microsoft.Azure.Management.Resources.ResourceManagementClient GetLegacyResourceManagementClient()
        {
            return Microsoft.Azure.Test.TestBase.GetServiceClient<Microsoft.Azure.Management.Resources.ResourceManagementClient>(this.csmTestFactory);
        }
        
        private Microsoft.Azure.Subscriptions.SubscriptionClient GetLegacySubscriptionClient()
        {
            return Microsoft.Azure.Test.TestBase.GetServiceClient<Microsoft.Azure.Subscriptions.SubscriptionClient>(this.csmTestFactory);
        }

        private Microsoft.Azure.Management.ResourceManager.ResourceManagementClient GetResourceManagerResourceManagementClient(RestTestFramework.MockContext context)
        {
            return context.GetServiceClient<Microsoft.Azure.Management.ResourceManager.ResourceManagementClient>(RestTestFramework.TestEnvironmentFactory.GetTestEnvironment());
        }

        private void SetupManagementClients(RestTestFramework.MockContext context)
        {
            Microsoft.Azure.Management.ResourceManager.ResourceManagementClient ResourceManagerResourceManagementClient = GetResourceManagerResourceManagementClient(context);
            this.ResourceManagementClient = this.GetResourceManagementClient();
            this.SubscriptionClient = this.GetSubscriptionClient();
            this.GalleryClient = this.GetGalleryClient();
            this.NetworkManagementClient = this.GetNetworkManagementClient(context);
            this.ComputeManagementClient = this.GetComputeManagementClient(context);
            this.StorageManagementClient = this.GetStorageManagementClient(context);
            this.AuthorizationManagementClient = this.GetAuthorizationManagementClient();
            this.InsightsManagementClient = this.GetInsightsManagementClient();
            this.RedisManagementClient = this.GetRedisManagementClient(context);

            helper.SetupManagementClients(
                ResourceManagerResourceManagementClient,
                ResourceManagementClient,
                SubscriptionClient,
                GalleryClient,
                this.NetworkManagementClient,
                this.ComputeManagementClient,
                this.StorageManagementClient,
                this.AuthorizationManagementClient,
                this.InsightsManagementClient,
                this.RedisManagementClient);
        }

        private AuthorizationManagementClient GetAuthorizationManagementClient()
        {
            return TestBase.GetServiceClient<AuthorizationManagementClient>(this.csmTestFactory);
        }

        private ResourceManagementClient GetResourceManagementClient()
        {
            return TestBase.GetServiceClient<ResourceManagementClient>(this.csmTestFactory);
        }

        private SubscriptionClient GetSubscriptionClient()
        {
            return TestBase.GetServiceClient<SubscriptionClient>(this.csmTestFactory);
        }

        private NetworkManagementClient GetNetworkManagementClient(RestTestFramework.MockContext context)
        {
            return context.GetServiceClient<NetworkManagementClient>(RestTestFramework.TestEnvironmentFactory.GetTestEnvironment());
        }

        private StorageManagementClient GetStorageManagementClient(RestTestFramework.MockContext context)
        {
            return context.GetServiceClient<StorageManagementClient>(RestTestFramework.TestEnvironmentFactory.GetTestEnvironment());
        }

        private GalleryClient GetGalleryClient()
        {
            return TestBase.GetServiceClient<GalleryClient>(this.csmTestFactory);
        }
        
        private InsightsManagementClient GetInsightsManagementClient()
        {
            return TestBase.GetServiceClient<InsightsManagementClient>(this.csmTestFactory);
        }

        private RedisManagementClient GetRedisManagementClient(RestTestFramework.MockContext context)
        {
            return context.GetServiceClient<RedisManagementClient>(RestTestFramework.TestEnvironmentFactory.GetTestEnvironment());
        }

        private ComputeManagementClient GetComputeManagementClient(RestTestFramework.MockContext context)
        {
            return context.GetServiceClient<ComputeManagementClient>(RestTestFramework.TestEnvironmentFactory.GetTestEnvironment());
        }
    }
}
