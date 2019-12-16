﻿// ReSharper disable ArrangeThisQualifier
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using ExRam.Gremlinq.Core.GraphElements;
using Gremlin.Net.Process.Traversal;
using LanguageExt;
using NullGuard;

namespace ExRam.Gremlinq.Core
{
    internal enum QuerySemantics
    {
        None = 0,
        Vertex = 1,
        Edge = 2,
        Property = 3,
        VertexProperty = 4
    }

    internal static class GremlinQuery
    {
        internal static readonly IImmutableList<Step> AnonymousNoneSteps = ImmutableList<Step>.Empty.Add(NoneStep.Instance);

        public static IGremlinQuery<object> Anonymous(IGremlinQueryEnvironment environment)
        {
            return Create<object>(
                ImmutableList<Step>.Empty,
                environment,
                false);
        }

        public static IGremlinQuery<TElement> Create<TElement>(IGremlinQueryEnvironment environment)
        {
            return Create<TElement>(
                ImmutableList<Step>.Empty,
                environment,
                true);
        }

        public static IGremlinQuery<TElement> Create<TElement>(IImmutableList<Step> steps, IGremlinQueryEnvironment environment, bool surfaceVisible)
        {
            return new GremlinQuery<TElement, object, object, object, object, object>(
                steps,
                environment,
                QuerySemantics.None,
                surfaceVisible);
        }
    }

    internal abstract class GremlinQueryBase
    {
        private static readonly ConcurrentDictionary<Type, Func<IImmutableList<Step>, IGremlinQueryEnvironment, bool, IGremlinQuery>> QueryTypes = new ConcurrentDictionary<Type, Func<IImmutableList<Step>, IGremlinQueryEnvironment, bool, IGremlinQuery>>();

        private static readonly Type[] SupportedInterfaceDefinitions = typeof(GremlinQuery<,,,,,>)
            .GetInterfaces()
            .Select(iface => iface.IsGenericType ? iface.GetGenericTypeDefinition() : iface)
            .ToArray();

        protected GremlinQueryBase(
            IImmutableList<Step> steps,
            IGremlinQueryEnvironment environment,
            QuerySemantics semantics,
            bool surfaceVisible)
        {
            Steps = steps;
            Semantics = semantics;
            Environment = environment;
            SurfaceVisible = surfaceVisible;
        }

        protected TTargetQuery ChangeQueryType<TTargetQuery>()
        {
            var targetQueryType = typeof(TTargetQuery);

            if (targetQueryType.IsAssignableFrom(GetType()) && targetQueryType.IsGenericType && Semantics != QuerySemantics.None)
                return (TTargetQuery)(object)this;

            var genericTypeDef = targetQueryType.IsGenericType
                ? targetQueryType.GetGenericTypeDefinition()
                : targetQueryType;

            if (!SupportedInterfaceDefinitions.Contains(genericTypeDef))
                throw new NotSupportedException($"Cannot change the query type to {targetQueryType}.");

            var constructor = QueryTypes.GetOrAdd(
                targetQueryType,
                closureType =>
                {
                    var semantics = closureType.GetQuerySemantics();
                    var genericType = typeof(GremlinQuery<,,,,,>).MakeGenericType(
                        GetMatchingType(closureType, "TElement", "TVertex", "TEdge", "TProperty", "TArray"),
                        GetMatchingType(closureType, "TOutVertex", "TAdjacentVertex"),
                        GetMatchingType(closureType, "TInVertex"),
                        GetMatchingType(closureType, "TValue"),
                        GetMatchingType(closureType, "TMeta"),
                        GetMatchingType(closureType, "TQuery"));

                    var stepsParameter = Expression.Parameter(typeof(IImmutableList<Step>));
                    var environmentParameter = Expression.Parameter(typeof(IGremlinQueryEnvironment));
                    var surfaceVisibleParameter = Expression.Parameter(typeof(bool));

                    return Expression
                        .Lambda<Func<IImmutableList<Step>, IGremlinQueryEnvironment, bool, IGremlinQuery>>(
                            Expression.New(
                                genericType.GetConstructor(new[]
                                {
                                    stepsParameter.Type,
                                    environmentParameter.Type,
                                    typeof(QuerySemantics),
                                    surfaceVisibleParameter.Type
                                }),
                                stepsParameter,
                                environmentParameter,
                                Expression.Constant(semantics, typeof(QuerySemantics)),
                                surfaceVisibleParameter),
                            stepsParameter,
                            environmentParameter,
                            surfaceVisibleParameter)
                        .Compile();
                });

            return (TTargetQuery)constructor(Steps, Environment, SurfaceVisible);
        }

        private static Type GetMatchingType(Type interfaceType, params string[] argumentNames)
        {
            if (interfaceType.IsGenericType)
            {
                var genericArguments = interfaceType.GetGenericArguments();
                var genericTypeDefinitionArguments = interfaceType.GetGenericTypeDefinition().GetGenericArguments();

                foreach (var argumentName in argumentNames)
                {
                    for (var i = 0; i < genericTypeDefinitionArguments.Length; i++)
                    {
                        if (genericTypeDefinitionArguments[i].ToString() == argumentName)
                            return genericArguments[i];
                    }
                }
            }

            return typeof(object);
        }

        protected internal bool SurfaceVisible { get; }
        protected internal QuerySemantics Semantics { get; }
        protected internal IImmutableList<Step> Steps { get; }
        protected internal IGremlinQueryEnvironment Environment { get; }
    }

    internal sealed partial class GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> : GremlinQueryBase where TMeta : class
    {
        private sealed class OrderBuilder : IOrderBuilderWithBy<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>
        {
            private readonly GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> _query;

            public OrderBuilder(GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> query)
            {
                _query = query;
            }

            GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> IOrderBuilderWithBy<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.Build() => _query;

            IOrderBuilderWithBy<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.By(Expression<Func<TElement, object>> projection) => By(projection, Gremlin.Net.Process.Traversal.Order.Incr);

            IOrderBuilderWithBy<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.By(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> traversal) => By(traversal, Gremlin.Net.Process.Traversal.Order.Incr);

            IOrderBuilderWithBy<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.By(ILambda lambda) => By(lambda);

            IOrderBuilderWithBy<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.By(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> traversal) => By(traversal, Gremlin.Net.Process.Traversal.Order.Incr);

            IOrderBuilderWithBy<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.By(ILambda lambda) => By(lambda);

            IOrderBuilderWithBy<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.ByDescending(Expression<Func<TElement, object>> projection) => By(projection, Gremlin.Net.Process.Traversal.Order.Decr);

            IOrderBuilderWithBy<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.ByDescending(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> traversal) => By(traversal, Gremlin.Net.Process.Traversal.Order.Decr);

            IOrderBuilderWithBy<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>> IOrderBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.ByDescending(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> traversal) => By(traversal, Gremlin.Net.Process.Traversal.Order.Decr);

            private OrderBuilder By(Expression<Func<TElement, object>> projection, Order order)
            {
                if (projection.Body.StripConvert() is MemberExpression memberExpression)
                    return new OrderBuilder(_query.AddStep(new OrderStep.ByMemberStep(_query.Environment.Model.PropertiesModel.GetIdentifier(memberExpression.Member), order), _query.Semantics));

                throw new ExpressionNotSupportedException(projection);
            }

            private OrderBuilder By(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> traversal, Order order) => new OrderBuilder(_query.AddStep(new OrderStep.ByTraversalStep(traversal(_query.Anonymize()), order), _query.Semantics));

            private OrderBuilder By(ILambda lambda) => new OrderBuilder(_query.AddStep(new OrderStep.ByLambdaStep(lambda), _query.Semantics));
        }

        private sealed class ChooseBuilder<TTargetQuery, TPickElement> :
            IChooseBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>,
            IChooseBuilderWithCondition<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TPickElement>,
            IChooseBuilderWithCase<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TPickElement, TTargetQuery>
            where TTargetQuery : IGremlinQuery
        {
            private readonly IGremlinQuery _targetQuery;
            private readonly GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> _sourceQuery;

            public ChooseBuilder(GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> sourceQuery, IGremlinQuery targetQuery)
            {
                _sourceQuery = sourceQuery;
                _targetQuery = targetQuery;
            }

            public IChooseBuilderWithCondition<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TNewPickElement> On<TNewPickElement>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery<TNewPickElement>> chooseTraversal)
            {
                return new ChooseBuilder<TTargetQuery, TNewPickElement>(
                    _sourceQuery,
                    _targetQuery.AsAdmin().AddStep(new ChooseOptionTraversalStep(chooseTraversal(_sourceQuery))));
            }

            public IChooseBuilderWithCase<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TPickElement, TNewTargetQuery> Case<TNewTargetQuery>(TPickElement element, Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TNewTargetQuery> continuation) where TNewTargetQuery : IGremlinQuery
            {
                return new ChooseBuilder<TNewTargetQuery, TPickElement>(
                    _sourceQuery,
                    _targetQuery.AsAdmin().AddStep(new OptionTraversalStep(element, continuation(_sourceQuery))));
            }

            public IChooseBuilderWithCaseOrDefault<TNewTargetQuery> Default<TNewTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TNewTargetQuery> continuation) where TNewTargetQuery : IGremlinQuery
            {
                return new ChooseBuilder<TNewTargetQuery, TPickElement>(
                    _sourceQuery,
                    _targetQuery.AsAdmin().AddStep(new OptionTraversalStep(default, continuation(_sourceQuery))));
            }

            public IChooseBuilderWithCase<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TPickElement, TTargetQuery> Case(TPickElement element, Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> continuation) => Case<TTargetQuery>(element, continuation);

            public IChooseBuilderWithCaseOrDefault<TTargetQuery> Default(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> continuation) => Default<TTargetQuery>(continuation);

            public TTargetQuery TargetQuery
            {
                get
                {
                    if (_targetQuery == null)
                        throw new InvalidOperationException();

                    return _targetQuery
                        .AsAdmin()
                        .ChangeQueryType<TTargetQuery>();
                }
            }
        }

        private sealed class GroupBuilder<TKey, TValue> :
            IGroupBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>,
            IGroupBuilderWithKey<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TKey>,
            IGroupBuilderWithKeyAndValue<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TKey, TValue>
        {
            private readonly GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> _sourceQuery;

            public GroupBuilder(GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> sourceQuery) : this(sourceQuery, default, default)
            {

            }

            public GroupBuilder(GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> sourceQuery, IGremlinQuery<TKey>? keyQuery, IGremlinQuery<TValue>? valueQuery)
            {
                KeyQuery = keyQuery;
                ValueQuery = valueQuery;
                _sourceQuery = sourceQuery;
            }

            IGroupBuilderWithKey<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TNewKey> IGroupBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>.ByKey<TNewKey>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery<TNewKey>> keySelector)
            {
                return new GroupBuilder<TNewKey, object>(
                    _sourceQuery,
                    keySelector(_sourceQuery),
                    default);
            }

            IGroupBuilderWithKeyAndValue<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TKey, TNewValue> IGroupBuilderWithKey<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TKey>.ByValue<TNewValue>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery<TNewValue>> valueSelector)
            {
                return new GroupBuilder<TKey, TNewValue>(
                    _sourceQuery,
                    KeyQuery,
                    valueSelector(_sourceQuery));
            }

            public IGremlinQuery<TKey> KeyQuery { get; }

            public IGremlinQuery<TValue> ValueQuery { get; }
        }

        private sealed partial class ProjectBuilderImpl<TProjectElement, TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7, TItem8, TItem9, TItem10, TItem11, TItem12, TItem13, TItem14, TItem15, TItem16> :
            IProjectBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>,
            IProjectDynamicBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>
        {
            private readonly GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> _sourceQuery;

            public ProjectBuilderImpl(GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> sourceQuery) : this(sourceQuery, ImmutableDictionary<string, IGremlinQuery>.Empty)
            {
            }

            public ProjectBuilderImpl(GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> sourceQuery, IImmutableDictionary<string, IGremlinQuery> projections)
            {
                _sourceQuery = sourceQuery;
                Projections = projections;
            }

            private ProjectBuilderImpl<TProjectElement, TNewItem1, TNewItem2, TNewItem3, TNewItem4, TNewItem5, TNewItem6, TNewItem7, TNewItem8, TNewItem9, TNewItem10, TNewItem11, TNewItem12, TNewItem13, TNewItem14, TNewItem15, TNewItem16> By<TNewItem1, TNewItem2, TNewItem3, TNewItem4, TNewItem5, TNewItem6, TNewItem7, TNewItem8, TNewItem9, TNewItem10, TNewItem11, TNewItem12, TNewItem13, TNewItem14, TNewItem15, TNewItem16>(Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> projection)
            {
                return By<TNewItem1, TNewItem2, TNewItem3, TNewItem4, TNewItem5, TNewItem6, TNewItem7, TNewItem8, TNewItem9, TNewItem10, TNewItem11, TNewItem12, TNewItem13, TNewItem14, TNewItem15, TNewItem16>($"Item{Projections.Count + 1}", projection);
            }

            private ProjectBuilderImpl<TProjectElement, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object> By(string name, Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> projection)
            {
                return By<object, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object>(name, projection);
            }

            private ProjectBuilderImpl<TProjectElement, TNewItem1, TNewItem2, TNewItem3, TNewItem4, TNewItem5, TNewItem6, TNewItem7, TNewItem8, TNewItem9, TNewItem10, TNewItem11, TNewItem12, TNewItem13, TNewItem14, TNewItem15, TNewItem16> By<TNewItem1, TNewItem2, TNewItem3, TNewItem4, TNewItem5, TNewItem6, TNewItem7, TNewItem8, TNewItem9, TNewItem10, TNewItem11, TNewItem12, TNewItem13, TNewItem14, TNewItem15, TNewItem16>(string name, Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> projection)
            {
                return new ProjectBuilderImpl<TProjectElement, TNewItem1, TNewItem2, TNewItem3, TNewItem4, TNewItem5, TNewItem6, TNewItem7, TNewItem8, TNewItem9, TNewItem10, TNewItem11, TNewItem12, TNewItem13, TNewItem14, TNewItem15, TNewItem16>(
                    _sourceQuery,
                    Projections.SetItem(name, projection(_sourceQuery)));
            }

            IProjectTupleBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement> IProjectBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>.ToTuple()
            {
                return this;
            }

            IProjectDynamicBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement> IProjectBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>.ToDynamic()
            {
                return this;
            }

            IProjectTupleBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement, TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7, TItem8, TItem9, TItem10, TItem11, TItem12, TItem13, TItem14, TItem15, TNewItem16> IProjectTupleBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement, TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7, TItem8, TItem9, TItem10, TItem11, TItem12, TItem13, TItem14, TItem15>.By<TNewItem16>(Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery<TNewItem16>> projection)
            {
                return By<TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7, TItem8, TItem9, TItem10, TItem11, TItem12, TItem13, TItem14, TItem15, TNewItem16>(projection);
            }

            IProjectTupleBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement, TNewItem1> IProjectTupleBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>.By<TNewItem1>(Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery<TNewItem1>> projection)
            {
                return By<TNewItem1, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object>(projection);
            }

            IProjectDynamicBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement> IProjectDynamicBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>.By(Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> projection)
            {
                return By($"Item{Projections.Count + 1}", projection);
            }

            IProjectDynamicBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement> IProjectDynamicBuilder<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TProjectElement>.By(string name, Func<GremlinQuery<TProjectElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> projection)
            {
                return By(name, projection);
            }

            public IImmutableDictionary<string, IGremlinQuery> Projections { get; }
        }

        public GremlinQuery(
            IImmutableList<Step> steps,
            IGremlinQueryEnvironment environment,
            QuerySemantics semantics,
            bool surfaceVisible) : base(steps, environment, semantics, surfaceVisible)
        {

        }

        private GremlinQuery<TEdge, TElement, object, object, object, object> AddE<TEdge>(TEdge newEdge)
        {
            return this
                .AddStep<TEdge, TElement, object, object, object, object>(new AddEStep(Environment.Model, newEdge), QuerySemantics.Edge)
                .AddOrUpdate(newEdge, true, false);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> AddOrUpdate(TElement element, bool add, bool allowExplicitCardinality)
        {
            var ret = this;
            var props = element.Serialize(
                Environment.Model.PropertiesModel,
                add
                    ? SerializationBehaviour.IgnoreOnAdd
                    : SerializationBehaviour.IgnoreOnUpdate);

            if (!add)
            {
                ret = ret.SideEffect(_ => _
                    .Properties<object, object, object>(
                        props
                            .Select(p => p.identifier)
                            .OfType<string>(),
                        Semantics)
                    .Drop());
            }

            foreach (var (property, identifier, value) in props)
            {
                foreach (var propertyStep in GetPropertySteps(property.PropertyType, identifier, value, allowExplicitCardinality))
                {
                    ret = ret.AddStep(propertyStep, Semantics);
                }
            }

            return ret;
        }

        private IEnumerable<PropertyStep> GetPropertySteps(Type propertyType, object key, object value, bool allowExplicitCardinality)
        {
            if (!propertyType.IsArray || propertyType == typeof(byte[]))
                yield return GetPropertyStep(key, value, allowExplicitCardinality ? Cardinality.Single : default);
            else
            {
                if (!allowExplicitCardinality)
                    throw new InvalidOperationException(/*TODO */);

                // ReSharper disable once PossibleNullReferenceException
                if (propertyType.GetElementType().IsInstanceOfType(value))
                {
                    yield return GetPropertyStep(key, value, Cardinality.List);
                }
                else
                {
                    foreach (var item in (IEnumerable)value)
                    {
                        yield return GetPropertyStep(key, item, Cardinality.List);
                    }
                }
            }
        }

        private PropertyStep GetPropertyStep(object key, object value, Cardinality cardinality)
        {
            var metaProperties = Array.Empty<object>();

            if (value is IVertexProperty && value is Property property)
            {
                metaProperties = property
                    .GetMetaProperties(Environment.Model.PropertiesModel)
                    .SelectMany(kvp => new[] { kvp.Key, kvp.Value })
                    .ToArray();

                value = property.GetValue();
            }

            return new PropertyStep(key, value, metaProperties, cardinality);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> AddStep(Step step, QuerySemantics querySemantics) => AddStep<TElement>(step, querySemantics);

        private GremlinQuery<TNewElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> AddStep<TNewElement>(Step step, QuerySemantics querySemantics) => AddStep<TNewElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>(step, querySemantics);

        private GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery> AddStep<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery>(Step step, QuerySemantics querySemantics) where TNewMeta : class => new GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery>(Steps.Insert(Steps.Count, step), Environment, querySemantics, SurfaceVisible);

        private GremlinQuery<TNewElement, object, object, object, object, object> AddStepWithObjectTypes<TNewElement>(Step step, QuerySemantics querySemantics) => AddStep<TNewElement, object, object, object, object, object>(step, querySemantics);

        private GremlinQuery<TVertex, object, object, object, object, object> AddV<TVertex>(TVertex vertex)
        {
            return this
                .AddStepWithObjectTypes<TVertex>(new AddVStep(Environment.Model, vertex), QuerySemantics.Vertex)
                .AddOrUpdate(vertex, true, true);
        }

        private TTargetQuery Aggregate<TStepLabel, TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TStepLabel, TTargetQuery> continuation)
            where TStepLabel : StepLabel, new()
            where TTargetQuery : IGremlinQuery
        {
            var stepLabel = new TStepLabel();

            return continuation(
                AddStep(new AggregateStep(stepLabel), QuerySemantics.None),
                stepLabel);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> And(params Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery>[] andTraversalTransformations)
        {
            if (andTraversalTransformations.Length == 0)
                return AddStep(AndStep.Infix, Semantics);

            var anonymous = Anonymize();
            var subQueries = default(List<IGremlinQuery>);

            foreach (var transformation in andTraversalTransformations)
            {
                var transformed = transformation(anonymous);

                if (transformed.IsNone())
                    return None();

                if (transformed != anonymous)
                    (subQueries ??= new List<IGremlinQuery>()).Add(transformed);
            }

            return (subQueries?.Count).GetValueOrDefault() == 0
                ? this
                : AddStep(new AndStep(subQueries.ToArray()), Semantics);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Anonymize(bool surfaceVisible = false) => Anonymize<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>(surfaceVisible);

        private GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery> Anonymize<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery>(bool surfaceVisible = false) where TNewMeta : class => new GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery>(ImmutableList<Step>.Empty, Environment, Semantics, surfaceVisible);

        private TTargetQuery As<TStepLabel, TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TStepLabel, TTargetQuery> continuation)
            where TStepLabel : StepLabel, new()
            where TTargetQuery : IGremlinQuery
        {
            if (Steps.LastOrDefault() is AsStep asStep && asStep.StepLabels.FirstOrDefault() is TStepLabel existingStepLabel)
                return continuation(this, existingStepLabel);

            var stepLabel = new TStepLabel();

            return continuation(
                As(stepLabel),
                stepLabel);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> As(params StepLabel[] stepLabels) => AddStep(new AsStep(stepLabels), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Barrier() => AddStep(BarrierStep.Instance, Semantics);

        private GremlinQuery<TTarget, object, object, object, object, object> BothV<TTarget>() => AddStepWithObjectTypes<TTarget>(BothVStep.Instance, QuerySemantics.Vertex);

        private GremlinQuery<TNewElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Cast<TNewElement>() => Cast<TNewElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>();

        private GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery> Cast<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery>() where TNewMeta : class => new GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, TNewPropertyValue, TNewMeta, TNewFoldedQuery>(Steps, Environment, Semantics, SurfaceVisible);

        private TTargetQuery Choose<TTargetQuery>(Expression<Func<TElement, bool>> predicate, Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> trueChoice, Option<Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery>> maybeFalseChoice = default) where TTargetQuery : IGremlinQuery
        {
            var gremlinExpression = predicate.ToGremlinExpression();

            if (gremlinExpression is TerminalGremlinExpression terminal)
            {
                if (terminal.Key == terminal.Parameter)
                {
                    var anonymous = Anonymize();
                    var trueQuery = trueChoice(anonymous);
                    var maybeFalseQuery = maybeFalseChoice.Map(falseChoice => (IGremlinQuery)falseChoice(anonymous));

                    return this
                        .AddStep(new ChoosePredicateStep(terminal.Predicate, trueQuery, maybeFalseQuery), QuerySemantics.None)
                        .ChangeQueryType<TTargetQuery>();
                }
            }

            throw new ExpressionNotSupportedException(predicate);
        }

        private TTargetQuery Choose<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> traversalPredicate, Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> trueChoice, Option<Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery>> maybeFalseChoice = default) where TTargetQuery : IGremlinQuery
        {
            var anonymous = Anonymize();
            var trueQuery = trueChoice(anonymous);
            var maybeFalseQuery = maybeFalseChoice.Map(falseChoice => (IGremlinQuery)falseChoice(anonymous));

            return maybeFalseQuery
                .BiFold(
                    AddStep(new ChooseTraversalStep(traversalPredicate(anonymous), trueQuery, maybeFalseQuery), QuerySemantics.None),
                    (query, falseQuery) => query,
                    (query, _) => query)
                .ChangeQueryType<TTargetQuery>();
        }

        private TTargetQuery Choose<TTargetQuery>(Func<IChooseBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>, IChooseBuilderWithCaseOrDefault<TTargetQuery>> continuation)
            where TTargetQuery : IGremlinQuery
        {
            return continuation(new ChooseBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, object>(Anonymize(), this)).TargetQuery;
        }

        private TTargetQuery Coalesce<TTargetQuery>(params Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery>[] traversals)
            where TTargetQuery : IGremlinQuery
        {
            var coalesceQueries = traversals
                .Select(traversal => (IGremlinQuery)traversal(Anonymize()))
                .ToArray();

            return this
                .AddStep(new CoalesceStep(coalesceQueries), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Coin(double probability) => AddStep(new CoinStep(probability), Semantics);

        private GremlinQuery<TNewElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> ConfigureSteps<TNewElement>(Func<IImmutableList<Step>, IImmutableList<Step>> configurator) => new GremlinQuery<TNewElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>(configurator(Steps), Environment, Semantics, SurfaceVisible);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Dedup() => AddStep(DedupStep.Instance, Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Drop() => AddStep(DropStep.Instance, Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> DropProperties(string key)
        {
            return SideEffect(_ => _
                .Properties<object, object, object>(new[] { key }, QuerySemantics.Property)
                .Drop());
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Emit() => AddStep(EmitStep.Instance, Semantics);

        private TTargetQuery FlatMap<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> mapping) where TTargetQuery : IGremlinQuery
        {
            var mappedTraversal = mapping(Anonymize());

            return this
                .AddStep(new FlatMapStep(mappedTraversal), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private GremlinQuery<TElement[], object, object, object, object, TNewFoldedQuery> Fold<TNewFoldedQuery>() => AddStep<TElement[], object, object, object, object, TNewFoldedQuery>(FoldStep.Instance, QuerySemantics.None);

        private GremlinQuery<IDictionary<TKey, TValue>, object, object, object, object, object> Group<TKey, TValue>(Func<IGroupBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>, IGroupBuilderWithKeyAndValue<IGremlinQuery, TKey, TValue>> projection)
        {
            var group = projection(new GroupBuilder<object, object>(Anonymize()));

            return this
                .AddStep<IDictionary<TKey, TValue>, object, object, object, object, object>(GroupStep.Instance, QuerySemantics.None)
                .AddStep(new GroupStep.ByTraversalStep(group.KeyQuery), QuerySemantics.None)
                .AddStep(new GroupStep.ByTraversalStep(group.ValueQuery), QuerySemantics.None);
        }

        private GremlinQuery<IDictionary<TKey, object>, object, object, object, object, object> Group<TKey>(Func<IGroupBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>, IGroupBuilderWithKey<IGremlinQuery, TKey>> projection)
        {
            var group = projection(new GroupBuilder<object, object>(Anonymize()));

            return this
                .AddStep<IDictionary<TKey, object>, object, object, object, object, object>(GroupStep.Instance, QuerySemantics.None)
                .AddStep(new GroupStep.ByTraversalStep(group.KeyQuery), QuerySemantics.None);
        }

        private object[] GetKeys(IEnumerable<LambdaExpression> projections)
        {
            return GetKeys(projections
                .Select(projection =>
                {
                    if (projection.Body.StripConvert() is MemberExpression memberExpression)
                        return memberExpression;

                    throw new ExpressionNotSupportedException(projection);
                }));
        }

        private object[] GetKeys(IEnumerable<MemberExpression> projections)
        {
            return projections
                .Select(projection => Environment.Model.PropertiesModel.GetIdentifier(projection.Member))
                .ToArray();
        }

        // ReSharper disable once SuggestBaseTypeForParameter
        private static IEnumerable<Step> GetStepsForKeys(object[] keys)
        {
            var hasYielded = false;

            foreach (var t in keys.OfType<T>())
            {
                if (T.Id.Equals(t))
                    yield return IdStep.Instance;
                else if (T.Label.Equals(t))
                    yield return LabelStep.Instance;
                else
                    throw new ExpressionNotSupportedException();

                hasYielded = true;
            }

            var stringKeys = keys
                .OfType<string>()
                .ToArray();

            if (stringKeys.Length > 0 || !hasYielded)
                yield return new ValuesStep(stringKeys);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Has(Expression expression, P predicate)
        {
            if (predicate.EqualsConstant(false))
                return None();

            if (expression is MemberExpression memberExpression)
                return AddStep(new HasStep(Environment.Model.PropertiesModel.GetIdentifier(memberExpression.Member), predicate), Semantics);

            throw new ExpressionNotSupportedException(expression);//TODO: Lift?
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Has(Expression expression, IGremlinQuery traversal)
        {
            if (expression is MemberExpression memberExpression)
                return AddStep(new HasStep(Environment.Model.PropertiesModel.GetIdentifier(memberExpression.Member), traversal), Semantics);

            throw new ExpressionNotSupportedException(expression);//TODO: Lift?
        }

        private GremlinQuery<object, object, object, object, object, object> Id() => AddStepWithObjectTypes<object>(IdStep.Instance, QuerySemantics.None);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Identity() => this;

        private GremlinQuery<TNewElement, object, object, object, object, object> Inject<TNewElement>(IEnumerable<TNewElement> elements) => AddStepWithObjectTypes<TNewElement>(new InjectStep(elements.Cast<object>().ToArray()), QuerySemantics.None);

        private GremlinQuery<TNewElement, object, object, object, object, object> InV<TNewElement>() => AddStepWithObjectTypes<TNewElement>(InVStep.Instance, QuerySemantics.Vertex);

        private GremlinQuery<string, object, object, object, object, object> Key() => AddStepWithObjectTypes<string>(KeyStep.Instance, QuerySemantics.None);

        private GremlinQuery<string, object, object, object, object, object> Label() => AddStepWithObjectTypes<string>(LabelStep.Instance, QuerySemantics.None);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Limit(long count)
        {
            return AddStep(
                count == 1
                    ? LimitStep.LimitGlobal1
                    : new LimitStep(count, Scope.Global),
                Semantics);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> LimitLocal(long count)
        {
            return AddStep(
                count == 1
                    ? LimitStep.LimitLocal1
                    : new LimitStep(count, Scope.Local),
                Semantics);
        }

        private TTargetQuery Local<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> localTraversal)
            where TTargetQuery : IGremlinQuery
        {
            var localTraversalQuery = localTraversal(Anonymize());

            return this
                .AddStep(new LocalStep(localTraversalQuery), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private TTargetQuery Map<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> mapping) where TTargetQuery : IGremlinQuery
        {
            var mappedTraversal = mapping(Anonymize());

            return this
                .AddStep(new MapStep(mappedTraversal), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Match(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery>[] matchTraversals) => AddStep(new MatchStep(matchTraversals.Select(traversal => traversal(Anonymize()))), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> None()
        {
            return this.IsIdentity()
                ? new GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>(GremlinQuery.AnonymousNoneSteps, Environment, Semantics, SurfaceVisible)
                : AddStep(NoneStep.Instance, Semantics);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Not(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> notTraversal)
        {
            var transformed = notTraversal(Anonymize());

            return transformed.IsIdentity()
                ? None()
                : transformed.IsNone()
                    ? this
                    : AddStep(new NotStep(transformed), Semantics);
        }

        private GremlinQuery<TTarget, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> OfType<TTarget>(IGraphElementModel model)
        {
            if (typeof(TTarget).IsAssignableFrom(typeof(TElement)))
                return Cast<TTarget>();

            return model
                .TryGetFilterLabels(typeof(TTarget), Environment.Options.GetValue(GremlinqOption.FilterLabelsVerbosity))
                .Match(
                    labels => labels.Length > 0
                        ? Steps.Count > 0 && Steps[Steps.Count - 1] is HasLabelStep
                            ? ConfigureSteps<TTarget>(steps => steps.SetItem(steps.Count - 1, new HasLabelStep(labels)))
                            : AddStep<TTarget>(new HasLabelStep(labels), Semantics)
                        : Cast<TTarget>(),
                    () => AddStep<TTarget>(NoneStep.Instance, Semantics));
        }

        private TTargetQuery Optional<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> optionalTraversal) where TTargetQuery : IGremlinQuery
        {
            var optionalQuery = optionalTraversal(Anonymize());

            return this
                .AddStep(new OptionalStep(optionalQuery), Semantics)
                .ChangeQueryType<TTargetQuery>();
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Or(params Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery>[] orTraversalTransformations)
        {
            if (orTraversalTransformations.Length == 0)
                return AddStep(OrStep.Infix, Semantics);

            var anonymous = Anonymize();
            var subQueries = default(List<IGremlinQuery>);

            foreach (var transformation in orTraversalTransformations)
            {
                var transformed = transformation(anonymous);

                if (transformed == anonymous)
                    return this;

                if (!transformed.IsNone())
                    (subQueries ??= new List<IGremlinQuery>()).Add(transformed);
            }

            return (subQueries?.Count).GetValueOrDefault() == 0
                ? None()
                : AddStep(new OrStep(subQueries.ToArray()), Semantics);
        }

        private TTargetQuery Order<TTargetQuery>(Func<IOrderBuilder<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>, IOrderBuilderWithBy<TTargetQuery>> projection) where TTargetQuery : IGremlinQuery => projection(new OrderBuilder(this.AddStep(OrderStep.Instance, Semantics))).Build();

        private TTargetQuery Order<TTargetQuery>(Func<IOrderBuilder<TElement, GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>>, IOrderBuilderWithBy<TElement, TTargetQuery>> projection) where TTargetQuery : IGremlinQuery<TElement> => projection(new OrderBuilder(this.AddStep(OrderStep.Instance, Semantics))).Build();

        private GremlinQuery<TTarget, object, object, object, object, object> OtherV<TTarget>() => AddStepWithObjectTypes<TTarget>(OtherVStep.Instance, QuerySemantics.Vertex);

        private GremlinQuery<TTarget, object, object, object, object, object> OutV<TTarget>() => AddStepWithObjectTypes<TTarget>(OutVStep.Instance, QuerySemantics.Vertex);

        private GremlinQuery<TResult, object, object, object, object, object> Project<TActualElement, TResult>(Func<IProjectBuilder<GremlinQuery<TActualElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TActualElement>, IProjectResult> continuation)
        {
            var projections = continuation(new ProjectBuilderImpl<TActualElement, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object>(Anonymize<TActualElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>(true)))
                .Projections
                .OrderBy(x => x.Key)
                .ToArray();

            var ret = this
                .AddStepWithObjectTypes<TResult>(new ProjectStep(projections.Select(x => x.Key).ToArray()), QuerySemantics.None);

            foreach (var projection in projections)
            {
                ret = ret.AddStep(new ProjectStep.ByTraversalStep(projection.Value), QuerySemantics.None);
            }

            return ret;
        }

        private GremlinQuery<TNewElement, object, object, TNewPropertyValue, TNewMeta, object> Properties<TNewElement, TNewPropertyValue, TNewMeta>(QuerySemantics querySemantics, params LambdaExpression[] projections) where TNewMeta : class
        {
            return Properties<TNewElement, TNewPropertyValue, TNewMeta>(
                projections
                    .Select(projection => projection.GetMemberInfo().Name),
                querySemantics);
        }

        private GremlinQuery<TNewElement, object, object, TNewPropertyValue, TNewMeta, object> Properties<TNewElement, TNewPropertyValue, TNewMeta>(IEnumerable<string> keys, QuerySemantics querySemantics) where TNewMeta : class => AddStep<TNewElement, object, object, TNewPropertyValue, TNewMeta, object>(new PropertiesStep(keys.ToArray()), querySemantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Property<TSource, TValue>(Expression<Func<TSource, TValue>> projection, [AllowNull] object value)
        {
            if (projection.Body.StripConvert() is MemberExpression memberExpression && Environment.Model.PropertiesModel.GetIdentifier(memberExpression.Member) is string identifier)
                return Property(identifier, value);

            throw new ExpressionNotSupportedException(projection);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Property(string key, [AllowNull] object value)
        {
            return value == null
                ? DropProperties(key)
                : AddStep(new PropertyStep(key, value), Semantics);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Range(long low, long high) => AddStep(new RangeStep(low, high), Semantics);

        private TTargetQuery Repeat<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> repeatTraversal)
            where TTargetQuery : IGremlinQuery
        {
            var repeatQuery = repeatTraversal(Anonymize());

            return this
                .AddStep(new RepeatStep(repeatQuery), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private TTargetQuery RepeatUntil<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> repeatTraversal, Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> untilTraversal)
            where TTargetQuery : IGremlinQuery
        {
            var anonymous = Anonymize();
            var repeatQuery = repeatTraversal(anonymous);

            return this
                .AddStep(new RepeatStep(repeatQuery), QuerySemantics.None)
                .AddStep(new UntilStep(untilTraversal(anonymous)), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private TTargetQuery UntilRepeat<TTargetQuery>(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery> repeatTraversal, Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> untilTraversal)
            where TTargetQuery : IGremlinQuery
        {
            var anonymous = Anonymize();
            var repeatQuery = repeatTraversal(anonymous);

            return this
                .AddStep(new UntilStep(untilTraversal(anonymous)), QuerySemantics.None)
                .AddStep(new RepeatStep(repeatQuery), QuerySemantics.None)
                .ChangeQueryType<TTargetQuery>();
        }

        private GremlinQuery<TSelectedElement, object, object, object, object, object> Select<TSelectedElement>(StepLabel<TSelectedElement> stepLabel) => AddStepWithObjectTypes<TSelectedElement>(new SelectStep(stepLabel), stepLabel.QuerySemantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> SideEffect(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> sideEffectTraversal) => AddStep(new SideEffectStep(sideEffectTraversal(Anonymize())), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Skip(long count) => AddStep(new SkipStep(count), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> SumGlobal() => AddStep(SumStep.Global, QuerySemantics.None);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> SumLocal() => AddStep(SumStep.Local, QuerySemantics.None);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Tail(long count) => AddStep(new TailStep(count, Scope.Global), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> TailLocal(long count) => AddStep(new TailStep(count, Scope.Local), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Times(int count) => AddStep(new TimesStep(count), Semantics);

        private GremlinQuery<TNewElement, TNewOutVertex, TNewInVertex, object, object, object> To<TNewElement, TNewOutVertex, TNewInVertex>(StepLabel stepLabel) => AddStep<TNewElement, TNewOutVertex, TNewInVertex, object, object, object>(new AddEStep.ToLabelStep(stepLabel), Semantics);

        private TTargetQuery Unfold<TTargetQuery>() => AddStep(UnfoldStep.Instance, Semantics).ChangeQueryType<TTargetQuery>();

        private TTargetQuery Union<TTargetQuery>(params Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, TTargetQuery>[] unionTraversals) where TTargetQuery : IGremlinQuery
        {
            var unionQueries = unionTraversals
                .Select(unionTraversal => (IGremlinQuery)unionTraversal(Anonymize()))
                .ToArray();

            return this
                .AddStep(new UnionStep(unionQueries), Semantics)
                .ChangeQueryType<TTargetQuery>();
        }

        private IValueGremlinQuery<TNewPropertyValue> Value<TNewPropertyValue>() => AddStepWithObjectTypes<TNewPropertyValue>(ValueStep.Instance, QuerySemantics.None);

        private GremlinQuery<TNewElement, object, object, object, object, object> ValueMap<TNewElement>(string[] keys) => AddStepWithObjectTypes<TNewElement>(new ValueMapStep(keys), QuerySemantics.None);

        private GremlinQuery<TNewElement, object, object, object, object, object> ValueMap<TNewElement>(IEnumerable<LambdaExpression> projections)
        {
            var projectionsArray = projections
                .ToArray();

            var stringKeys = GetKeys(projectionsArray)
                .OfType<string>()
                .ToArray();

            if (stringKeys.Length != projectionsArray.Length)
                throw new ExpressionNotSupportedException();

            return AddStepWithObjectTypes<TNewElement>(new ValueMapStep(stringKeys), QuerySemantics.None);
        }

        private GremlinQuery<TValue, object, object, object, object, object> ValuesForKeys<TValue>(object[] keys)
        {
            var stepsArray = GetStepsForKeys(keys)
                .ToArray();

            switch (stepsArray.Length)
            {
                case 0:
                    throw new ExpressionNotSupportedException();
                case 1:
                    return AddStepWithObjectTypes<TValue>(stepsArray[0], QuerySemantics.None);
                default:
                    return AddStepWithObjectTypes<TValue>(new UnionStep(stepsArray.Select(step => Anonymize().AddStep(step, QuerySemantics.None))), QuerySemantics.None);
            }
        }

        private GremlinQuery<TValue, object, object, object, object, object> ValuesForProjections<TValue>(IEnumerable<LambdaExpression> projections) => ValuesForKeys<TValue>(GetKeys(projections));

        private GremlinQuery<VertexProperty<TNewPropertyValue>, object, object, TNewPropertyValue, object, object> VertexProperties<TNewPropertyValue>(LambdaExpression[] projections) => Properties<VertexProperty<TNewPropertyValue>, TNewPropertyValue, object>(QuerySemantics.VertexProperty, projections);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> VertexProperty(LambdaExpression projection, [AllowNull] object value)
        {
            if (projection.Body.StripConvert() is MemberExpression memberExpression)
            {
                var identifier = Environment.Model.PropertiesModel.GetIdentifier(memberExpression.Member);

                if (value == null)
                {
                    if (identifier is string stringKey)
                        return DropProperties(stringKey);
                }
                else
                {
                    var ret = this;

                    foreach(var propertyStep in GetPropertySteps(memberExpression.Type, identifier, value, true))
                    {
                        ret = ret.AddStep(propertyStep, Semantics);
                    }

                    return ret;
                }
            }

            throw new ExpressionNotSupportedException(projection);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where(ILambda lambda) => AddStep(new FilterStep(lambda), Semantics);

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where(Func<GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery>, IGremlinQuery> filterTraversal)
        {
            var anonymous = Anonymize();
            var filtered = filterTraversal(anonymous);

            return filtered.IsIdentity()
                ? this
                : filtered.IsNone()
                    ? None()
                    : AddStep(new WhereTraversalStep(filtered), Semantics);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where(LambdaExpression predicate)
        {
            try
            {
                return Where(predicate.ToGremlinExpression());
            }
            catch (ExpressionNotSupportedException ex)
            {
                throw new ExpressionNotSupportedException(ex);
            }
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where<TProjection>(Expression<Func<TElement, TProjection>> predicate, Func<IGremlinQuery<TProjection>, IGremlinQuery> propertyTraversal) => Has(predicate.Body, propertyTraversal(Anonymize<TProjection, object, object, object, object, object>()));

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where(GremlinExpression gremlinExpression)
        {
            if (gremlinExpression is OrGremlinExpression or)
            {
                return Or(
                    _ => _.Where(or.Operand1),
                    _ => _.Where(or.Operand2));
            }

            if (gremlinExpression is AndGremlinExpression and)
            {
                return this
                    .Where(and.Operand1)
                    .Where(and.Operand2);
            }

            if (gremlinExpression is NotGremlinExpression not)
                return Not(_ => _.Where(not.Negate()));

            if (gremlinExpression is TerminalGremlinExpression terminal)
            {
                var effectivePredicate = terminal.Predicate is TextP textP
                    ? textP.WorkaroundLimitations(Environment.Options)
                    : terminal.Predicate;

                switch (terminal.Key)
                {
                    case MemberExpression leftMemberExpression:
                    {
                        var leftMemberExpressionExpression = leftMemberExpression.Expression.StripConvert();

                        if (leftMemberExpressionExpression == terminal.Parameter)
                        {
                            // x => x.Value == P.xy(...)
                            if (leftMemberExpression.IsPropertyValue())
                                return AddStep(new HasValueStep(effectivePredicate),Semantics);

                            if (leftMemberExpression.IsPropertyKey())
                                return Where(__ => __.Key().Where(effectivePredicate));

                            if (leftMemberExpression.IsVertexPropertyLabel())
                                return Where(__ => __.Label().Where(effectivePredicate));
                        }
                        else if (leftMemberExpressionExpression is MemberExpression leftLeftMemberExpression) 
                        {
                            // x => x.Name.Value == P.xy(...)
                            if (leftMemberExpression.IsPropertyValue())
                                leftMemberExpression = leftLeftMemberExpression;    //TODO: What else ?
                        }
                        else
                            break;

                        // x => x.Name == P.xy(...)
                        return Where(leftMemberExpression, effectivePredicate);
                    }
                    case ParameterExpression leftParameterExpression when terminal.Parameter == leftParameterExpression:
                    {
                        // x => x == P.xy(...)
                        return Where(effectivePredicate);
                    }
                    case MethodCallExpression methodCallExpression:
                    {
                        var targetExpression = methodCallExpression.Object;

                        if (targetExpression != null && typeof(IDictionary<string, object>).IsAssignableFrom(targetExpression.Type) && methodCallExpression.Method.Name == "get_Item")
                        {
                            return AddStep(new HasStep(methodCallExpression.Arguments[0].GetValue(), effectivePredicate), Semantics);
                        }

                        break;
                    }
                }
            }

            throw new ExpressionNotSupportedException();
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where(MemberExpression expression, P predicate)
        {
            return predicate.ContainsOnlyStepLabels()
                ? Has(expression, Anonymize().Where(predicate))
                : Has(expression, predicate);
        }

        private GremlinQuery<TElement, TOutVertex, TInVertex, TPropertyValue, TMeta, TFoldedQuery> Where(P predicate)
        {
            return predicate.ContainsOnlyStepLabels()
                ? AddStep(new WherePredicateStep(predicate), Semantics)
                : AddStep(new IsStep(predicate), Semantics);
        }
    }
}
