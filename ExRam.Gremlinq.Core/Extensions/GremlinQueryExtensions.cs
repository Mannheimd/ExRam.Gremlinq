﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ExRam.Gremlinq.Core
{
    public static class GremlinQueryExtensions
    {
        public static ValueTask<TElement[]> ToArrayAsync<TElement>(this IGremlinQueryBase<TElement> query, CancellationToken ct = default)
        {
            return query.ToAsyncEnumerable().ToArrayAsync(ct);
        }

        public static ValueTask<TElement> FirstAsync<TElement>(this IGremlinQueryBase<TElement> query, CancellationToken ct = default)
        {
            return query.ToAsyncEnumerable().FirstAsync(ct);
        }

        public static ValueTask<TElement> FirstOrDefaultAsync<TElement>(this IGremlinQueryBase<TElement> query, CancellationToken ct = default)
        {
            return query.ToAsyncEnumerable().FirstOrDefaultAsync(ct);
        }

        internal static IGremlinQueryBase AddStep(this IGremlinQueryBase query, Step step)
        {
            return query.AsAdmin().InsertStep(query.AsAdmin().Steps.Count, step);
        }

        internal static bool IsNone(this IGremlinQueryBase query)
        {
            return query is GremlinQueryBase gremlinQuery && ReferenceEquals(gremlinQuery.Steps, GremlinQuery.AnonymousNoneSteps);
        }

        internal static bool IsIdentity(this IGremlinQueryBase query)
        {
            return query is GremlinQueryBase gremlinQuery && gremlinQuery.Steps.Count == 0;
        }

        /// <summary>
        /// Creates a continuation delegate from a <paramref name="sourceQuery"/> and <paramref name="targetQuery"/>
        /// when <paramref name="sourceQuery"/> is a prefix of <paramref name="targetQuery"/>.
        /// </summary>
        /// <typeparam name="TSourceQuery"></typeparam>
        /// <typeparam name="TTargetQuery"></typeparam>
        /// <param name="sourceQuery"></param>
        /// <param name="targetQuery"></param>
        /// <returns></returns>
        public static Func<TSourceQuery, TTargetQuery> CreateContinuationFrom<TSourceQuery, TTargetQuery>(this TSourceQuery sourceQuery, TTargetQuery targetQuery)
            where TSourceQuery : IGremlinQueryBase
            where TTargetQuery : IGremlinQueryBase
        {
            var sourceAdmin = sourceQuery.AsAdmin();
            var targetAdmin = targetQuery.AsAdmin();

            if (!ReferenceEquals(sourceAdmin.Environment, targetAdmin.Environment))
                throw new ArgumentException($"{nameof(sourceQuery)} and {nameof(targetQuery)} don't agree on environments.");

            if (sourceAdmin.Steps.Count > targetAdmin.Steps.Count)
                throw new ArgumentException($"{nameof(sourceQuery)} must not have more steps than {nameof(targetQuery)}.");

            if (sourceAdmin.Steps.Count == targetAdmin.Steps.Count)
            {
                return _ => _
                    .AsAdmin()
                    .ChangeQueryType<TTargetQuery>();
            }

            using (var e1 = sourceAdmin.Steps.GetEnumerator())
            {
                using (var e2 = targetAdmin.Steps.GetEnumerator())
                {
                    var list = new List<Step>();

                    while (true)
                    {
                        if (e2.MoveNext())
                        {
                            if (e1.MoveNext())
                            {
                                if (!ReferenceEquals(e1.Current, e2.Current))
                                    throw new ArgumentException($"{nameof(sourceQuery)} is not a proper prefix of {nameof(targetQuery)}.");
                            }
                            else
                            {
                                do
                                {
                                    list.Add(e2.Current);
                                } while (e2.MoveNext());

                                break;
                            }
                        }
                        else
                            break;
                    }

                    return _ => _
                        .AsAdmin()
                        .AddSteps(list.ToArray())
                        .AsAdmin()
                        .ChangeQueryType<TTargetQuery>();
                }
            }
        }
     }
}
