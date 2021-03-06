﻿/////////////////////////////////////////////////////////////////////
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

        /// <summary>
        /// GET TreeNode passing the ID
        /// </summary>
        [HttpGet]
        [Route("api/forge/bim360/hubs/{hubId}/users")]
        public async Task<JArray> GetProjectByUser(string hubId)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            dynamic bearer = await oauth.AuthenticateAsync(Credentials.GetAppSetting("FORGE_CLIENT_ID"), Credentials.GetAppSetting("FORGE_CLIENT_SECRET"), "client_credentials", new Scope[] { Scope.AccountRead, Scope.DataRead });
            JArray users = await GetUsers(hubId, bearer.access_token);
            foreach (dynamic user in users)
            {
                user.projects = new JArray();
                user.projects_count = 0;

                if (user.uid == null) continue; // not activated yet
                dynamic projects = await GetProjectsAsync(hubId, (string)user.uid, (string)bearer.access_token);
                if (projects == null) continue;
                foreach (dynamic project in projects.data)
                {
                    dynamic projectInfo = new JObject();
                    projectInfo.name = project.attributes.name;
                    projectInfo.id = project.id;
                    user.projects.Add(projectInfo);
                }
                user.projects_count = ((JArray)user.projects).Count;
            }

            return new JArray(users.OrderByDescending(u => (int)u["projects_count"]));
        }

        public async Task<JObject> GetProjectsAsync(string hubId, string userId, string accessToken, int attempt = 0)
        {
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("project/v1/hubs/{hubId}/projects?page[limit]=100", RestSharp.Method.GET);
            request.AddParameter("hubId", hubId, ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + accessToken);
            request.AddHeader("x-user-id", userId);
            IRestResponse response = await client.ExecuteTaskAsync(request);
            if (response.StatusCode == HttpStatusCode.OK)
                return JObject.Parse(response.Content);
            else if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt > 5) return null;
                int retryAfter = int.Parse(response.Headers.ToList().Find(h => h.Name == "Retry-After").Value.ToString());
                System.Threading.Thread.Sleep(retryAfter * 1000);
                return await GetProjectsAsync(hubId, userId, accessToken, ++attempt);
            }
            return null;
        }

        /*public async Task<JArray> GetAccountProjectsAsync(string accountId)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            dynamic bearer = await oauth.AuthenticateAsync(Credentials.GetAppSetting("FORGE_CLIENT_ID"), Credentials.GetAppSetting("FORGE_CLIENT_SECRET"), "client_credentials", new Scope[] { Scope.AccountRead });

            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("hq/v1/accounts/{account_id}/projects", RestSharp.Method.GET);
            request.AddParameter("account_id", accountId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddHeader("Authorization", "Bearer " + bearer.access_token);
            IRestResponse response = await client.ExecuteTaskAsync(request);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            return JArray.Parse(response.Content);
        }*/

        private async Task<string> GetContainerAsync(string hubId, string projectId)
        {
            ProjectsApi projectsApi = new ProjectsApi();
            projectsApi.Configuration.AccessToken = Credentials.TokenInternal;
            var project = await projectsApi.GetProjectAsync(hubId, projectId);
            var issuesContainer = project.data.relationships.issues.data;
            if (issuesContainer.type != "issueContainerId") return null;
            return issuesContainer["id"];
        }

        public async Task<JArray> GetUsers(string hubId, string accessToken, JArray users = null, int offset = 0)
        {
            users = (users == null ? new JArray() : users);
            RestClient client = new RestClient(BASE_URL);
            RestRequest request = new RestRequest("/hq/v1/accounts/{account_id}/users?limit={limit}&offset={offset}&field=name,uid", RestSharp.Method.GET);
            request.AddParameter("account_id", hubId.Replace("b.", string.Empty), ParameterType.UrlSegment);
            request.AddParameter("limit", 100);
            request.AddParameter("offset", offset);
            request.AddHeader("Authorization", "Bearer " + accessToken);
            IRestResponse response = await client.ExecuteTaskAsync(request);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            JArray page = JArray.Parse(response.Content);
            users.Merge(page);
            if (page.Count >= 100) await GetUsers(hubId, accessToken, users, offset += 100);
            return users;
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

        [HttpGet]
        [Route("api/forge/bim360/hubs/{hubId}/projects/{projectId}/quality-issues")]
        public async Task<JArray> GetQualityIssuesAsync(string hubId, string projectId)
        {
            Credentials = await Credentials.FromSessionAsync(base.Request.Cookies, Response.Cookies);
            if (Credentials == null) { return null; }

            TwoLeggedApi oauth = new TwoLeggedApi();
            dynamic bearer = await oauth.AuthenticateAsync(Credentials.GetAppSetting("FORGE_CLIENT_ID"), Credentials.GetAppSetting("FORGE_CLIENT_SECRET"), "client_credentials", new Scope[] { Scope.AccountRead });

            JArray users = await GetUsers(hubId, bearer.access_token);

            JArray issues = new JArray();
            dynamic response = null;
            int offset = 0;
            do
            {
                response = await GetResourceAsync(await GetContainerAsync(hubId, projectId), "quality-issues", offset);
                issues.Merge(response.data);
                offset += 50;
            } while (!string.IsNullOrEmpty((string)response.links.next));

            foreach (dynamic issue in issues)
            {
                if (issue.attributes.created_by != null) issue.attributes.owner = GetUserById(users, (string)issue.attributes.owner);
                if (issue.attributes.created_by != null) issue.attributes.created_by = GetUserById(users, (string)issue.attributes.created_by);
                if (issue.attributes.created_by != null) issue.attributes.assigned_to = GetUserById(users, (string)issue.attributes.assigned_to);
                if (issue.attributes.created_by != null) issue.attributes.answered_by = GetUserById(users, (string)issue.attributes.answered_by);
            }

            return issues;
        }

        public JObject GetUserById(JArray users, string userId)
        {
            return JObject.FromObject((from dynamic u in users where u.uid == userId select new { name = u.name, image_url = u.image_url }).FirstOrDefault());
        }
    }
}