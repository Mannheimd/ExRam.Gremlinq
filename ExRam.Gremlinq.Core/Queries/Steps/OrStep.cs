﻿using System;

namespace ExRam.Gremlinq.Core
{
    public sealed class OrStep : LogicalStep
    {
        public static readonly OrStep Infix = new OrStep(Array.Empty<IGremlinQueryBase>());

        public OrStep(IGremlinQueryBase[] traversals) : base("or", traversals)
        {
        }
    }
}
