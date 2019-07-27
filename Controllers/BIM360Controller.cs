/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Autodesk.Forge;
using Autodesk.Forge.Model;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using RestSharp;

namespace forgeSample.Controllers
{
    public class DataManagementController : ControllerBase
    {
        /// <summary>
        /// Credentials on this request
        /// </summary>
        private Credentials Credentials { get; set; }

        /// <summary>
        /// GET TreeNode passing the ID
        /// </summary>
        [HttpGet]
        [Route("api/forge/datamanagement/projects")]
        public async Task<JArray> GetProjectsAsync()
        {
            Credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (Credentials == null) { return null; }

            // array of projects
            JArray allProjects = new JArray();

            // the API SDK
            HubsApi hubsApi = new HubsApi();
            hubsApi.Configuration.AccessToken = Credentials.TokenInternal;
            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = Credentials.TokenInternal;

            var hubs = await hubsApi.GetHubsAsync();
            foreach (KeyValuePair<string, dynamic> hubInfo in new DynamicDictionaryItems(hubs.data))
            {
                string hubType = (string)hubInfo.Value.attributes.extension.type;
                if (hubType != "hubs:autodesk.bim360:Account") continue; // skip non-BIM360 hub

                string hubId = (string)hubInfo.Value.id;
                var projects = await projectsApi.GetHubProjectsAsync(hubId);
                foreach (KeyValuePair<string, dynamic> projectInfo in new DynamicDictionaryItems(projects.data))
                    allProjects.Add(JObject.FromObject(new { hub = new { id = hubInfo.Value.id, name = hubInfo.Value.attributes.name }, project = new { id = projectInfo.Value.id, name = projectInfo.Value.attributes.name } }));
            }
            return new JArray(allProjects.OrderBy(obj => (string)obj["project"]["name"]));
        }

        private const string BASE_URL = "https://developer.api.autodesk.com";

        private async Task<string> GetContainerAsync(string hubId, string projectId)
        {
            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = Credentials.TokenInternal;
            var project = await projectsApi.GetProjectAsync(hubId, projectId);
            var issuesContainer = project.data.relationships.issues.data;
            if (issuesContainer.type != "issueContainerId") return null;
            return issuesContainer["id"];
        }

        private async Task<JObject> GetResourceAsync(string containerId, string resource, int offset = 0)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/issues/v1/containers/{container_id}/{resource}?page[limit]=50&page[offset]={offset}", RestSharp.Method.GET);
            request.AddParameter("container_id", containerId, ParameterType.UrlSegment);
            request.AddParameter("resource", resource, ParameterType.UrlSegment);
            request.AddParameter("offset", offset, ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + Credentials.TokenInternal);
            IRestResponse response = await client.ExecuteTaskAsync(request);
            if (response.StatusCode != HttpStatusCode.OK) throw new Exception("Cannot request " + resource);
            return JObject.Parse(response.Content);
        }

        /*[HttpGet]
        [Route("api/forge/bim360/hubs/{hubId}/projects/{projectId}/root-causes")]
        public async Task<JArray> GetRootCausesAsync(string hubId, string projectId)
        {
            Credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (Credentials == null) { return null; }

            return await GetResourceAsync(await GetContainerAsync(hubId, projectId), "root-causes");
        }*/

        [HttpGet]
        [Route("api/forge/bim360/hubs/{hubId}/projects/{projectId}/quality-issues")]
        public async Task<JArray> GetQualityIssuesAsync(string hubId, string projectId)
        {
            Credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (Credentials == null) { return null; }

            JArray issues = new JArray();
            dynamic response = null;
            int offset = 0;
            do
            {
                response = await GetResourceAsync(await GetContainerAsync(hubId, projectId), "quality-issues", offset);
                issues.Merge(response.data);
                offset += 50;
            } while (!string.IsNullOrEmpty((string)response.links.next));


            return issues;
        }
    }
}