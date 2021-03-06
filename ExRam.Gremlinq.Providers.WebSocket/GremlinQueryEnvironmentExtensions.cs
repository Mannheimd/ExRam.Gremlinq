﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExRam.Gremlinq.Providers.WebSocket;
using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Gremlin.Net.Process.Traversal;
using Gremlin.Net.Structure.IO.GraphSON;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExRam.Gremlinq.Core
{
    public static class GremlinQueryEnvironmentExtensions
    {
        private sealed class WebSocketGremlinQueryExecutor : IGremlinQueryExecutor, IDisposable
        {
            private readonly string _alias;
            private readonly Dictionary<string, string> _aliasArgs;
            private readonly Lazy<Task<IGremlinClient>> _lazyGremlinClient;

            public WebSocketGremlinQueryExecutor(
                Func<CancellationToken, Task<IGremlinClient>> clientFactory,
                string alias = "g")
            {
                _alias = alias;
                _aliasArgs = new Dictionary<string, string> { {"g", _alias} };
                _lazyGremlinClient = new Lazy<Task<IGremlinClient>>(() => clientFactory(default), LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public void Dispose()
            {
                _lazyGremlinClient.Value.Dispose();
            }

            public IAsyncEnumerable<object> Execute(object serializedQuery, IGremlinQueryEnvironment environment)
            {
                return AsyncEnumerable.Create(Core);

                async IAsyncEnumerator<object> Core(CancellationToken ct)
                {
                    var results = default(ResultSet<JToken>);

                    if (serializedQuery is GroovyGremlinQuery groovyScript)
                    {
                        Log(groovyScript, environment);

                        try
                        {
                            var client = await _lazyGremlinClient
                                .Value;

                            results = await client
                                .SubmitAsync<JToken>($"{_alias}.{groovyScript.Script}", groovyScript.Bindings)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            environment.Logger.LogError(
                                "Error executing Gremlin query {0}:\r\n{1}",
                                groovyScript.Script,
                                ex);

                            throw;
                        }
                    }
                    else if (serializedQuery is Bytecode bytecode)
                    {
                        Log(bytecode.ToGroovy(), environment);

                        var requestMsg = RequestMessage.Build(Tokens.OpsBytecode)
                            .Processor(Tokens.ProcessorTraversal)
                            .OverrideRequestId(Guid.NewGuid())
                            .AddArgument(Tokens.ArgsGremlin, bytecode)
                            .AddArgument(Tokens.ArgsAliases, _aliasArgs)
                            .Create();

                        try
                        {
                            var client = await _lazyGremlinClient
                                .Value;

                            results = await client
                                .SubmitAsync<JToken>(requestMsg)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            environment.Logger.LogError(
                                "Error executing Gremlin query {0}:\r\n{1}",
                                JsonConvert.SerializeObject(requestMsg),
                                ex);

                            throw;
                        }
                    }
                    else
                        throw new ArgumentException($"Cannot handle serialized query of type {serializedQuery.GetType()}.");

                    if (results != null)
                    {
                        foreach (var obj in results)
                        {
                            yield return obj;
                        }
                    }
                }
            }

            private void Log(GroovyGremlinQuery query, IGremlinQueryEnvironment environment)
            {
                var logLevel = environment.Options.GetValue(WebSocketGremlinqOptions.QueryLogLogLevel);
                var verbosity = environment.Options.GetValue(WebSocketGremlinqOptions.QueryLogVerbosity);

                if (environment.Logger.IsEnabled(logLevel))
                {
                    environment.Logger.Log(
                        logLevel,
                        "Executing Gremlin query {0}.",
                        JsonConvert.SerializeObject(
                            new
                            {
                                query.Script,
                                Bindings = (verbosity & QueryLogVerbosity.IncludeParameters) > QueryLogVerbosity.QueryOnly
                                    ? query.Bindings
                                    : null
                            }));
                }
            }
        }

        private sealed class WebSocketGremlinQueryEnvironmentBuilderImpl : IWebSocketGremlinQueryEnvironmentBuilder
        {
            private readonly Uri? _uri;
            private readonly string _alias;
            private readonly SerializationFormat _format;
            private readonly IGremlinQueryEnvironment _environment;
            private readonly ConnectionPoolSettings _connectionPoolSettings;
            private readonly (string username, string password)? _auth;
            private readonly Func<IGremlinClient, IGremlinClient> _clientTransformation;
            private readonly ImmutableDictionary<Type, IGraphSONSerializer> _additionalSerializers;
            private readonly ImmutableDictionary<string, IGraphSONDeserializer> _additionalDeserializers;

            public WebSocketGremlinQueryEnvironmentBuilderImpl(
                IGremlinQueryEnvironment environment,
                Uri? uri,
                SerializationFormat format,
                (string username, string password)? auth,
                string @alias,
                Func<IGremlinClient, IGremlinClient> clientTransformation,
                ImmutableDictionary<Type, IGraphSONSerializer> additionalSerializers,
                ImmutableDictionary<string, IGraphSONDeserializer> additionalDeserializers,
                ConnectionPoolSettings connectionPoolSettings)
            {
                _uri = uri;
                _auth = auth;
                _alias = alias;
                _format = format;
                _environment = environment;
                _additionalSerializers = additionalSerializers;
                _additionalDeserializers = additionalDeserializers;
                _connectionPoolSettings = connectionPoolSettings;
                _clientTransformation = clientTransformation;
            }

            public IWebSocketGremlinQueryEnvironmentBuilder At(Uri uri)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, uri, _format, _auth, _alias, _clientTransformation, _additionalSerializers, _additionalDeserializers, _connectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder ConfigureGremlinClient(Func<IGremlinClient, IGremlinClient> transformation)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, _format, _auth, _alias, _ => transformation(_clientTransformation(_)), _additionalSerializers, _additionalDeserializers, _connectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder SetSerializationFormat(SerializationFormat version)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, version, _auth, _alias, _clientTransformation, _additionalSerializers, _additionalDeserializers, _connectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder AuthenticateBy(string username, string password)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, _format, (username, password), _alias, _clientTransformation, _additionalSerializers, _additionalDeserializers, _connectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder SetAlias(string alias)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, _format, _auth, alias, _clientTransformation, _additionalSerializers, _additionalDeserializers, _connectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder ConfigureConnectionPool(Action<ConnectionPoolSettings> transformation)
            {
                var newConnectionPoolSettings = new ConnectionPoolSettings
                {
                    MaxInProcessPerConnection = _connectionPoolSettings.MaxInProcessPerConnection,
                    PoolSize = _connectionPoolSettings.PoolSize
                };

                transformation(newConnectionPoolSettings);

                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, _format, _auth, _alias, _clientTransformation, _additionalSerializers, _additionalDeserializers, newConnectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder AddGraphSONSerializer(Type type, IGraphSONSerializer serializer)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, _format, _auth, _alias, _clientTransformation, _additionalSerializers.SetItem(type, serializer), _additionalDeserializers, _connectionPoolSettings);
            }

            public IWebSocketGremlinQueryEnvironmentBuilder AddGraphSONDeserializer(string typename, IGraphSONDeserializer deserializer)
            {
                return new WebSocketGremlinQueryEnvironmentBuilderImpl(_environment, _uri, _format, _auth, _alias, _clientTransformation, _additionalSerializers, _additionalDeserializers.SetItem(typename, deserializer), _connectionPoolSettings);
            }

            public IGremlinQueryEnvironment Build()
            {
                if (_uri == null)
                    throw new InvalidOperationException($"No valid Gremlin endpoint found. Configure {nameof(GremlinQuerySource.g)} with {nameof(ConfigureWebSocket)} and use {nameof(At)} on the builder to set a valid Gremlin endpoint.");

                if (!"ws".Equals(_uri.Scheme, StringComparison.OrdinalIgnoreCase) && !"wss".Equals(_uri.Scheme, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Expected the Uri-Scheme to be either \"ws\" or \"wss\".");

                return _environment
                    .UseExecutor(
                        new WebSocketGremlinQueryExecutor(
                            async ct => _clientTransformation(await Task.Run(
                                () => new GremlinClient(
                                    new GremlinServer(
                                        (_uri.Host + _uri.AbsolutePath).TrimEnd('/'),
                                        _uri.Port,
                                        "wss".Equals(_uri.Scheme, StringComparison.OrdinalIgnoreCase),
                                        _auth?.username,
                                        _auth?.password),
                                    _format == SerializationFormat.GraphSonV2
                                        ? new GraphSON2Reader(_additionalDeserializers)
                                        : (GraphSONReader)new GraphSON3Reader(_additionalDeserializers),
                                    _format == SerializationFormat.GraphSonV2
                                        ? new GraphSON2Writer(_additionalSerializers)
                                        : (GraphSONWriter)new GraphSON3Writer(_additionalSerializers),
                                    _format == SerializationFormat.GraphSonV2
                                        ? GremlinClient.GraphSON2MimeType
                                        : GremlinClient.DefaultMimeType,
                                    _connectionPoolSettings),
                                ct)),
                            _alias));
            }
        }

        public static IGremlinQueryEnvironment ConfigureWebSocket(
            this IGremlinQueryEnvironment environment,
            Func<IWebSocketGremlinQueryEnvironmentBuilder, IGremlinQueryEnvironmentBuilder> builderAction)
        {
            return builderAction(new WebSocketGremlinQueryEnvironmentBuilderImpl(environment, default, SerializationFormat.GraphSonV3, null, "g", _ => _, ImmutableDictionary<Type, IGraphSONSerializer>.Empty, ImmutableDictionary<string, IGraphSONDeserializer>.Empty, new ConnectionPoolSettings()))
                .Build()
                .UseDeserializer(GremlinQueryExecutionResultDeserializer.FromJToken);
        }
    }
}
