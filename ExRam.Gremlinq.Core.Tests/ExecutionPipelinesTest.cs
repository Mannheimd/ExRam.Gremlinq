﻿using System;
using System.Linq;
using System.Threading.Tasks;
using ExRam.Gremlinq.Tests.Entities;
using FluentAssertions;
using Xunit;
using static ExRam.Gremlinq.Core.GremlinQuerySource;

namespace ExRam.Gremlinq.Core.Tests
{
    public class ExecutionPipelinesTest
    {
        private interface IFancyId
        {
            string Id { get; set; }
        }

        private class FancyId : IFancyId
        {
            public string Id { get; set; }
        }

        private class EvenMoreFancyId : FancyId
        {
        }

        [Fact]
        public async Task Echo()
        {
            var query = await g
                .UseModel(GraphModel
                    .FromBaseTypes<Vertex, Edge>())
                .UseExecutionPipeline(GremlinQueryExecutionPipeline.EchoGroovyString)
                .V<Person>()
                .Where(x => x.Age == 36)
                .Cast<string>()
                .FirstAsync();

            query
                .Should()
                .Be("g.V().hasLabel(_a).has(_b, _c)");
        }

        [Fact]
        public void Echo_wrong_type()
        {
            GremlinQuerySource.g
                .UseExecutionPipeline(GremlinQueryExecutionPipeline.EchoGroovyString)
                .V()
                .Awaiting(async _ => await _
                    .ToArrayAsync())
                .Should()
                .Throw<InvalidOperationException>();
        }

        [Fact]
        public void OverrideAtomSerializer()
        {
            g
                .UseModel(GraphModel
                    .FromBaseTypes<Vertex, Edge>())
                .ConfigureExecutionPipeline(_ => GremlinQueryExecutionPipeline
                    .EchoGroovyString
                    .ConfigureSerializer(_ => _
                        .OverrideAtomSerializer<FancyId>((key, assembler, overridden, recurse) => recurse(key.Id))))
                .V<Person>(new FancyId {Id = "someId"})
                .Should()
                .SerializeToGroovy("g.V(_a).hasLabel(_b)")
                .WithParameters("someId", "Person");
        }

        [Fact]
        public void OverrideAtomSerializer_recognizes_derived_type()
        {
            g
                .UseModel(GraphModel
                    .FromBaseTypes<Vertex, Edge>())
                .ConfigureExecutionPipeline(_ => GremlinQueryExecutionPipeline
                    .EchoGroovyString
                    .ConfigureSerializer(_ => _
                        .OverrideAtomSerializer<FancyId>((key, assembler, overridden, recurse) => recurse(key.Id))))
                .V<Person>(new EvenMoreFancyId { Id = "someId" })
                .Should()
                .SerializeToGroovy("g.V(_a).hasLabel(_b)")
                .WithParameters("someId", "Person");
        }

        [Fact]
        public void OverrideAtomSerializer_recognizes_interface()
        {
            g
                .UseModel(GraphModel
                    .FromBaseTypes<Vertex, Edge>())
                .ConfigureExecutionPipeline(_ => GremlinQueryExecutionPipeline
                    .EchoGroovyString
                    .ConfigureSerializer(_ => _
                        .OverrideAtomSerializer<IFancyId>((key, assembler, overridden, recurse) => recurse(key.Id))))
                .V<Person>(new FancyId { Id = "someId" })
                .Should()
                .SerializeToGroovy("g.V(_a).hasLabel(_b)")
                .WithParameters("someId", "Person");
        }

        [Fact]
        public void OverrideAtomSerializer_recognizes_interface_through_derived_type()
        {
            g
                .UseModel(GraphModel
                    .FromBaseTypes<Vertex, Edge>())
                .ConfigureExecutionPipeline(_ => GremlinQueryExecutionPipeline
                    .EchoGroovyString
                    .ConfigureSerializer(_ => _
                        .OverrideAtomSerializer<IFancyId>((key, assembler, overridden, recurse) => recurse(key.Id))))
                .V<Person>(new FancyId { Id = "someId" })
                .Should()
                .SerializeToGroovy("g.V(_a).hasLabel(_b)")
                .WithParameters("someId", "Person");
        }
    }
}
