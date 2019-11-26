﻿using Gremlin.Net.Process.Traversal;

namespace ExRam.Gremlinq.Core
{
    public sealed class SumStep : Step
    {
        public static readonly SumStep Local = new SumStep(Scope.Local);
        public static readonly SumStep Global = new SumStep(Scope.Global);

        public SumStep(Scope scope)
        {
            Scope = scope;
        }

        public Scope Scope { get; }
    }
}
