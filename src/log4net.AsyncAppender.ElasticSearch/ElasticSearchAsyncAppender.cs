﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using log4net.Core;

namespace log4net.AsyncAppender.ElasticSearch
{
    public class ElasticSearchAsyncAppender : HttpEndpointAsyncAppender
    {
        public string ConnectionString { get; set; }

        public string Index { get; set; }

        public bool IsRollingIndex { get; set; }

        public string Routing { get; set; }

        public Func<LoggingEvent, object> Projection { get; set; }

        #region Setup

        protected override void Configure()
        {
            if (!string.IsNullOrWhiteSpace(this.ConnectionString))
            {
                try
                {
                    var tokens = Parse(this.ConnectionString);

                    base.Scheme = TryGet(tokens, "Scheme");
                    base.UserName = TryGet(tokens, "UserName", "User");
                    base.Password = TryGet(tokens, "Password", "Pwd");
                    base.Host = TryGet(tokens, "Host", "Server");
                    base.Port = TryGet(tokens, "Port");
                    base.Path = TryGet(tokens, "Path");
                    base.Query = TryGet(tokens, "Query");
                    this.IsRollingIndex = bool.TryParse(TryGet(tokens, "Rolling"), out var isRollingIndex) ? isRollingIndex : false;
                    this.Index = TryGet(tokens, "Index");
                    this.Routing = TryGet(tokens, "Routing");

                    // Defaults

                    if (string.IsNullOrWhiteSpace(base.Scheme))
                        base.Scheme = "http";
                }
                catch
                {
                    this.ErrorHandler?.Error($"Invalid connection string.");
                }
            }

            if (this.Projection == null)
            {
                this.Projection = this.ProjectToElasticModel;
            }

            base.Configure();

            // Local functions

            string TryGet(Dictionary<string, string> tokens, params string[] coalescingSettingsKeys)
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
                if (string.IsNullOrWhiteSpace(this.Index))
                {
                    this.ErrorHandler?.Error($"Missing index.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                this.ErrorHandler?.Error("Error during validation", ex);
                return false;
            }

            return true;
        }

        protected virtual Dictionary<string, string> Parse(string connectionString)
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
            var baseUri = base.CreateEndpoint();

            var sb = new StringBuilder();
            sb.Append($"{baseUri.Scheme}://");

            var (userName, password) = TryExtractUserInfo(baseUri);

            if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password))
                sb.Append($"{userName}:{password}@");

            sb.Append($"{baseUri.Host}"); // "www.server.com"
            sb.Append($":{baseUri.Port}"); // ":80"

            // "/test/api/" or just "/"
            if (baseUri.AbsolutePath == "/")
                sb.Append(baseUri.AbsolutePath);
            else
                sb.Append($"{baseUri.AbsolutePath}/");

            var indexForRouting = this.IsRollingIndex
                ? $"{this.Index}-{DateTime.UtcNow.ToString("yyyy.MM.dd")}"
                : this.Index;

            sb.Append($"{Uri.EscapeUriString(indexForRouting)}");

            var routingForUri = string.IsNullOrWhiteSpace(this.Routing)
                ? string.Empty
                : $"?routing={this.Routing}";

            sb.Append($"/logEvent{Uri.EscapeUriString(routingForUri)}/_bulk");

            if (!string.IsNullOrWhiteSpace(baseUri.Query))
                sb.Append(Uri.EscapeUriString(baseUri.Query));

            var uri = new Uri(sb.ToString());
            return uri;
        }

        private (string userName, string password) TryExtractUserInfo(Uri uri)
        {
            string userName = null;
            string password = null;

            if (uri != null && !string.IsNullOrWhiteSpace(uri.UserInfo))
            {
                var items = uri.UserInfo.Split(':');
                if (items.Length > 0) userName = items[0];
                if (items.Length > 1) password = items[1];
            }

            return (userName, password);
        }

        protected override Task<HttpContent> GetHttpContentAsync(IReadOnlyList<LoggingEvent> events)
        {
            var json = this.SerializeAllToJson(events);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
            return Task.FromResult(content);
        }

        protected virtual string SerializeAllToJson(IReadOnlyList<LoggingEvent> events)
        {
            var sb = new StringBuilder();

            if (events.Count != 1)
                sb.AppendLine("{\"index\" : {} }");

            foreach (var e in events)
            {
                var json = this.SerializeToJson(e);
                sb.AppendLine(json);
            }

            return sb.ToString();
        }

        protected override string SerializeToJson(LoggingEvent @event)
        {
            var projection = this.Projection?.Invoke(@event) ?? @event;
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
