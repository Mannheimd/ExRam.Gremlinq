﻿using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using LanguageExt;

namespace ExRam.Gremlinq.Core
{
    public static class GraphElementModel
    {
        private sealed class GraphElementModelImpl : IGraphElementModel
        {
            public GraphElementModelImpl(IImmutableDictionary<Type, ElementMetadata> metaData)
            {
                Metadata = metaData;
            }

            public IImmutableDictionary<Type, ElementMetadata> Metadata { get; }
        }

        public static readonly IGraphElementModel Empty = new GraphElementModelImpl(ImmutableDictionary<Type, ElementMetadata>.Empty);

        private static readonly ConditionalWeakTable<IGraphElementModel, ConcurrentDictionary<Type, string[]>> DerivedLabels = new ConditionalWeakTable<IGraphElementModel, ConcurrentDictionary<Type, string[]>>();

        public static IGraphElementModel ConfigureLabels(this IGraphElementModel model, Func<Type, string, string> overrideTransformation)
        {
            return model.ConfigureMetadata(_ => _.ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => new ElementMetadata(overrideTransformation(kvp.Key, kvp.Value.Label))));
        }

        public static IGraphElementModel UseCamelCaseLabels(this IGraphElementModel model)
        {
            return model.ConfigureLabels((type, proposedLabel) => proposedLabel.ToCamelCase());
        }

        public static IGraphElementModel UseLowerCaseLabels(this IGraphElementModel model)
        {
            return model.ConfigureLabels((type, proposedLabel) => proposedLabel.ToLower());
        }

        internal static Step GetFilterStepOrNone(this IGraphElementModel model, Type type,
            FilterLabelsVerbosity verbosity, Func<string[], Step> stepFactory)
        {
            return model
                .TryGetFilterLabels(type, verbosity)
                .Map(stepFactory)
                .IfNone(NoneStep.Instance);
        }

        public static Option<string[]> TryGetFilterLabels(this IGraphElementModel model, Type type, FilterLabelsVerbosity filterLabelsVerbosity)
        {
            var labels = DerivedLabels
                .GetOrCreateValue(model)
                .GetOrAdd(
                    type,
                    closureType =>
                    {
                        return model.Metadata
                            .Where(kvp => !kvp.Key.IsAbstract && closureType.IsAssignableFrom(kvp.Key))
                            .Select(kvp => kvp.Value.Label)
                            .OrderBy(x => x)
                            .ToArray();
                    });


            return labels.Length == 0
                ? default(Option<string[]>)
                : labels.Length == model.Metadata.Count && filterLabelsVerbosity == FilterLabelsVerbosity.Minimum
                    ? Array.Empty<string>()
                    : labels;
        }

        private static IGraphElementModel ConfigureMetadata(this IGraphElementModel model, Func<IImmutableDictionary<Type, ElementMetadata>, IImmutableDictionary<Type, ElementMetadata>> transformation)
        {
            return new GraphElementModelImpl(
                transformation(model.Metadata));
        }
    }
}
