﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using log4net.Core;

namespace log4net.AsyncAppender.ElasticSearch
{
    public class ElasticSearchAsyncAppender : HttpEndpointAsyncAppender
    {
        public string? ConnectionString { get; set; }

        public string? ContentType { get; set; }

        public bool RequestSlimResponse { get; set; } = true;

        public string? Index { get; set; }

        public bool IsRollingIndex { get; set; }

        public string? Routing { get; set; }

        public Func<LoggingEvent, object>? Projection { get; set; }

        #region Setup

        protected override void Configure()
        {
            if (!string.IsNullOrWhiteSpace(ConnectionString))
            {
                try
                {
                    var tokens = Parse(ConnectionString);

                    Scheme = TryGet(tokens, "Scheme");
                    UserName = TryGet(tokens, "UserName", "User");
                    Password = TryGet(tokens, "Password", "Pwd");
                    Host = TryGet(tokens, "Host", "Server");
                    Port = TryGet(tokens, "Port");
                    Path = TryGet(tokens, "Path");
                    Query = TryGet(tokens, "Query");
                    IsRollingIndex = bool.TryParse(TryGet(tokens, "Rolling"), out var isRollingIndex) && isRollingIndex;
                    Index = TryGet(tokens, "Index");
                    Routing = TryGet(tokens, "Routing");

                    // Defaults

                    if (string.IsNullOrWhiteSpace(Scheme))
                        Scheme = "http";
                }
                catch
                {
                    ErrorHandler?.Error($"Invalid connection string.");
                }
            }

            if (Projection == null)
            {
                Projection = ProjectToElasticModel;
            }

            base.Configure();

            if (string.IsNullOrWhiteSpace(ContentType))
            {
                ContentType = "application/json";
            }

            // Local functions

            static string TryGet(Dictionary<string, string> tokens, params string[] coalescingSettingsKeys)
            {
                foreach (var key in coalescingSettingsKeys)
                    if (tokens.TryGetValue(key, out var value))
                        return value;

                return string.Empty;
            }
        }

        protected override bool ValidateSelf()
        {
            if (!base.ValidateSelf()) return false;

            try
            {
                if (string.IsNullOrWhiteSpace(Index))
                {
                    ErrorHandler?.Error($"Missing index.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ErrorHandler?.Error("Error during validation", ex);
                return false;
            }

            return true;
        }

        protected virtual Dictionary<string, string> Parse(string? connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var csBuilder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in csBuilder.Keys)
                settings[key] = csBuilder[key].ToString();

            return settings;
        }

        #endregion

        protected override Uri CreateEndpoint()
        {
            var uri = base.CreateEndpoint();
            var builder = new UriBuilder(uri);
            var query = System.Web.HttpUtility.ParseQueryString(builder.Query);

            var indexForRouting = IsRollingIndex
                ? $"{Index}-{DateTime.UtcNow:yyyy.MM.dd}"
                : Index;

            var basePath = uri.AbsolutePath;

            if (!basePath.EndsWith("/"))
                basePath += "/";

            var relative = new Uri($"{basePath}{indexForRouting}/logEvent/_bulk", UriKind.Relative);
            Uri.TryCreate(uri, relative, out uri);

            builder = new UriBuilder(uri);

            if (!string.IsNullOrWhiteSpace(Routing))
            {
                query["routing"] = Routing;
            }

            if (RequestSlimResponse)
            {
                query["filter_path"] = "took,errors";
            }

            builder.Query = System.Web.HttpUtility.UrlDecode(query.ToString());

            uri = builder.Uri;

            return uri;
        }

        protected override Task<HttpContent> GetHttpContentAsync(IReadOnlyList<LoggingEvent> events)
        {
            string json = SerializeAllToJson(events);

            HttpContent content = new StringContent(json);

            if (!string.IsNullOrWhiteSpace(ContentType))
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);

            return Task.FromResult(content);
        }

        protected virtual string SerializeAllToJson(IReadOnlyList<LoggingEvent> events)
        {
            // https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-bulk.html

            var sb = new StringBuilder();

            foreach (var e in events)
            {
                var json = SerializeToJson(e);
                sb.AppendLine("{\"index\" : {} }");
                sb.AppendLine(json);
            }

            sb.AppendLine();

            return sb.ToString();
        }

        protected override string SerializeToJson(LoggingEvent @event)
        {
            var projection = Projection?.Invoke(@event) ?? @event;
            var json = Utf8Json.JsonSerializer.ToJsonString(projection, Utf8Json.Resolvers.StandardResolver.CamelCase);
            return json;
        }

        protected virtual object ProjectToElasticModel(LoggingEvent @event)
        {
            var properties = @event.Properties ?? new Util.PropertiesDictionary();
            properties["@timestamp"] = @event.TimeStamp.ToUniversalTime().ToString("o");

            var projection = new
            {
                @event.LoggerName,
                @event.Domain,
                @event.Identity,
                @event.ThreadName,
                @event.UserName,
                TimeStamp = @event.TimeStamp.ToUniversalTime().ToString("O"),
                Exception = @event.ExceptionObject ?? new object(),
                Message = @event.RenderedMessage,
                Fix = @event.Fix.ToString(),
                Environment.MachineName,
                Level = @event.Level?.DisplayName,
                MessageObject = @event.MessageObject ?? new object(),
                @event.LocationInformation?.ClassName,
                @event.LocationInformation?.FileName,
                @event.LocationInformation?.LineNumber,
                @event.LocationInformation?.FullInfo,
                @event.LocationInformation?.MethodName,
                Properties = properties
            };

            return projection;
        }
    }
}
