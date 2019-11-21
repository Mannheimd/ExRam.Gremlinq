﻿using System;

namespace ExRam.Gremlinq.Core
{
    public static class GremlinQuerySerializerExtensions
    {
        public static IGremlinQuerySerializer UseCosmosDbWorkarounds(this IGremlinQuerySerializer serializer)
        {
            return serializer
                .OverrideAtomSerializer<CosmosDbKey>((key, assembler, overridden, recurse) => recurse(key.PartitionKey != null ? new[] { key.PartitionKey, key.Id } : (object)key.Id))
                .OverrideAtomSerializer<SkipStep>((step, assembler, overridden, recurse) => recurse(new RangeStep(step.Count, -1)))
                .OverrideAtomSerializer<LimitStep>((step, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    if (step.Count > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(step), "CosmosDb doesn't currently support values for 'Limit' outside the range of a 32-bit-integer.");

                    overridden(step);
                })
                .OverrideAtomSerializer<TailStep>((step, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    if (step.Count > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(step), "CosmosDb doesn't currently support values for 'Tail' outside the range of a 32-bit-integer.");

                    overridden(step);
                })
                .OverrideAtomSerializer<RangeStep>((step, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    if (step.Lower > int.MaxValue || step.Upper > int.MaxValue)
                        throw new ArgumentOutOfRangeException(nameof(step), "CosmosDb doesn't currently support values for 'Range' outside the range of a 32-bit-integer.");

                    overridden(step);
                })
                .OverrideAtomSerializer<long>((l, assembler, overridden, recurse) =>
                {
                    // Workaround for https://feedback.azure.com/forums/263030-azure-cosmos-db/suggestions/33998623-cosmosdb-s-implementation-of-the-tinkerpop-dsl-has
                    recurse((int)l);
                });
        }
    }
}
