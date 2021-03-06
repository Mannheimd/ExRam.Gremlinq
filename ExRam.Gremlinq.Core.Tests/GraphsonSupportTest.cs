﻿using FluentAssertions;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ExRam.Gremlinq.Core;
using ExRam.Gremlinq.Core.Tests;
using ExRam.Gremlinq.Tests.Entities;
using Newtonsoft.Json.Linq;
using VerifyXunit;
using Xunit;
using Xunit.Abstractions;
using static ExRam.Gremlinq.Core.GremlinQuerySource;

namespace ExRam.Gremlinq.Providers.Tests
{
    public class GraphsonSupportTest : VerifyBase
    {
        private sealed class MetaPoco
        {
            public string MetaKey { get; set; }
        }

        private sealed class PersonLanguageTuple
        {
            public Person Key { get; set; }
            public Language Value { get; set; }
        }

        private static readonly string SinglePersonJson;
        private static readonly string ArrayOfLanguages;
        private static readonly string SingleCompanyJson;
        private static readonly string SinglePersonStringId;
        private static readonly string SingleLanguageJson;
        private static readonly string SingleWorksFor;
        private static readonly string SingleTimeFrameJson;
        private static readonly string SinglePersonWithNullJson;
        private static readonly string TupleOfPersonLanguageJson;
        private static readonly string Graphson3ReferenceVertex;
        private static readonly string ThreeCompaniesAsTraverser;
        private static readonly string CountryWithMetaProperties;
        private static readonly string NestedArrayOfLanguagesJson;
        private static readonly string NamedTupleOfPersonLanguageJson;
        private static readonly string SingleTimeFrameWithNumbersJson;
        private static readonly string SinglePersonWithoutPhoneNumbersJson;
        private static readonly string SinglePersonLowercasePropertiesJson;
        private static readonly string Graphson3TupleOfPersonLanguageJson;

        private readonly IGremlinQuerySource _g;

        static GraphsonSupportTest()
        {
            SingleLanguageJson = GetJson("Single_Language");
            SingleCompanyJson = GetJson("Single_Company");
            ThreeCompaniesAsTraverser = GetJson("Traverser");
            SinglePersonJson = GetJson("Single_Person");
            SinglePersonWithNullJson = GetJson("Single_Person_with_null");
            SinglePersonLowercasePropertiesJson = GetJson("Single_Person_lowercase_properties");
            SinglePersonWithoutPhoneNumbersJson = GetJson("Single_Person_without_PhoneNumbers");
            TupleOfPersonLanguageJson = GetJson("Tuple_of_Person_Language");
            NamedTupleOfPersonLanguageJson = GetJson("Named_tuple_of_Person_Language");
            ArrayOfLanguages = GetJson("Array_of_Languages");
            NestedArrayOfLanguagesJson = GetJson("Nested_array_of_Languages");
            SingleTimeFrameJson = GetJson("Single_TimeFrame");
            SingleTimeFrameWithNumbersJson = GetJson("Single_TimeFrame_with_numbers");
            SingleWorksFor = GetJson("Single_WorksFor");
            Graphson3TupleOfPersonLanguageJson = GetJson("Graphson3_Tuple_of_Person_Language");
            Graphson3ReferenceVertex = GetJson("Graphson3ReferenceVertex");
            CountryWithMetaProperties = GetJson("Country_with_meta_properties");
            SinglePersonStringId = GetJson("Single_Person_String_Id");
        }

        public GraphsonSupportTest(ITestOutputHelper output) : base(output)
        {
            _g = g
                .ConfigureEnvironment(env => env.UseModel(GraphModel.FromBaseTypes<Vertex, Edge>(lookup => lookup
                    .IncludeAssembliesOfBaseTypes())));
        }

        [Fact]
        public void JToken_Load_does_not_reuse()
        {
            var token = JToken.Parse(SingleLanguageJson);

            var readToken1 = JToken.Load(new JTokenReader(token));
            var readToken2 = JToken.Load(new JTokenReader(token));

            readToken1
                .Should()
                .NotBeSameAs(readToken2);
        }

        [Fact]
        public async Task GraphSon3ReferenceVertex()
        {
            await _g
                .WithExecutor(Graphson3ReferenceVertex)
                .V()
                .Cast<JObject>()
                .Verify(this);
        }

        [Fact]
        public async Task Configured_property_name()
        {
            await _g
                .ConfigureEnvironment(env => env
                    .ConfigureModel(model => model
                        .ConfigureProperties(prop => prop
                            .ConfigureElement<Person>(conf => conf
                                .ConfigureName(x => x.Name, "replacement")))))
                .WithExecutor("[ { \"id\": 13, \"label\": \"Person\", \"type\": \"vertex\", \"properties\": { \"replacement\": [ { \"id\": 1, \"value\": \"nameValue\" } ] } } ]")
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task IsDescribedIn()
        {
            await _g
                .WithExecutor(SingleWorksFor)
                .E<WorksFor>()
                .Verify(this);
        }

        [Fact]
        public async Task DynamicData()
        {
            await _g
                .WithExecutor(SingleWorksFor)
                .V()
                .Project(_ => _
                    .ToDynamic()
                    .By("in!", __ => __.In()))
                .Verify(this);
        }

        [Fact]
        public async Task WorksFor_with_Graphson3()
        {
            await _g
                .WithExecutor("{\"@type\":\"g:List\",\"@value\":[{\"@type\":\"g:Edge\",\"@value\":{\"id\":{\"@type\":\"g:Int64\",\"@value\":23},\"label\":\"WorksFor\",\"inVLabel\":\"Company\",\"outVLabel\":\"Person\",\"inV\":\"companyId\",\"outV\":\"personId\",\"properties\":{\"Role\":{\"@type\":\"g:Property\",\"@value\":{\"key\":\"Role\",\"value\":\"Admin\"}},\"ActiveFrom\":{\"@type\":\"g:Property\",\"@value\":{\"key\":\"ActiveFrom\",\"value\":{\"@type\":\"g:Int64\",\"@value\":1523879885819}}}}}}]}")
                .E<WorksFor>()
                .Verify(this);
        }

        [Fact]
        public async Task Empty1()
        {
            await _g
                .WithExecutor("[]")
                .V()
                .Drop()
                .Verify(this);
        }

        [Fact]
        public async Task Empty2()
        {
            await _g
                .WithExecutor("[]")
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task String_Ids()
        {
            await _g
                .WithExecutor("[ \"id1\", \"id2\" ]")
                .V()
                .Id()
                .Verify(this);
        }

        [Fact]
        public async Task String_Ids2()
        {
            await _g
                .WithExecutor("[ \"1\", \"2\" ]")
                .V()
                .Id()
                .Verify(this);
        }

        [Fact]
        public async Task Int_Ids()
        {
            await _g
                .WithExecutor("[ 1, 2 ]")
                .V()
                .Id()
                .Verify(this);
        }

        [Fact]
        public async Task Empty_to_ints()
        {
            await Verify(await _g
                .WithExecutor("[{ \"Item1\": [], \"Item2\": [] }]")
                .V<(int[] ints, string[] strings)>()
                .ToArrayAsync());   //Must be Verify(...).
        }

        [Fact]
        public async Task Mixed_Ids()
        {
            await _g
                .WithExecutor("[ 1, \"id2\" ]")
                .V()
                .Id()
                .Verify(this);
        }

        [Fact]
        public async Task DateTime_is_UTC()
        {
            await _g
                .WithExecutor(SingleCompanyJson)
                .V<Company>()
                .Verify(this);
        }

        [Fact]
        public async Task Language_unknown_type()
        {
            await _g
                .WithExecutor(SingleLanguageJson)
                .V<object>()
                .Verify(this);
        }

        [Fact]
        public async Task Language_unknown_type_without_model()
        {
            await _g
                .ConfigureEnvironment(env => env
                    .UseModel(GraphModel.Empty))
                .WithExecutor(SingleLanguageJson)
                .V()
                .Cast<object>()
                .Verify(this);
        }

        [Fact]
        public async Task Language_strongly_typed()
        {
            await _g
                .WithExecutor(SingleLanguageJson)
                .V<Language>()
                .Verify(this);
        }

        [Fact]
        public async Task Language_strongly_typed_without_model()
        {
            await _g
                .ConfigureEnvironment(env => env
                    .UseModel(GraphModel.Empty))
                .WithExecutor(SingleLanguageJson)
                .V()
                .Cast<Language>()
                .Verify(this);
        }

        [Fact]
        public async Task Language_to_generic_vertex()
        {
            await _g
                .WithExecutor(SingleLanguageJson)
                .V<Vertex>()
                .Verify(this);
        }

        [Fact]
        public async Task Languages_to_object()
        {
            await _g
                .WithExecutor(ArrayOfLanguages)
                .V<object>()
                .Verify(this);
        }

        [Fact]
        public async Task Person_strongly_typed()
        {
            await _g
                .WithExecutor(SinglePersonJson)
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task Person_with_null()
        {
            await _g
                .WithExecutor(SinglePersonWithNullJson)
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task Person_StringId()
        {
            await _g
                .WithExecutor(SinglePersonStringId)
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task Person_lowercase_strongly_typed()
        {
            await _g
                .WithExecutor(SinglePersonLowercasePropertiesJson)
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task Person_without_PhoneNumbers_strongly_typed()
        {
            await _g
                .WithExecutor(SinglePersonWithoutPhoneNumbersJson)
                .V<Person>()
                .Verify(this);
        }

        [Fact]
        public async Task TimeFrame_strongly_typed()
        {
            await _g
                .WithExecutor(SingleTimeFrameJson)
                .V<TimeFrame>()
                .Verify(this);
        }

        [Fact(Skip = "Not standard behaviour!")]
        public async Task TimeFrame_with_numbers_strongly_typed()
        {
            await _g
                .WithExecutor(SingleTimeFrameWithNumbersJson)
                .V<TimeFrame>()
                .Verify(this);
        }

        [Fact]
        public async Task Language_by_vertex_inheritance()
        {
            await _g
                .WithExecutor(SingleLanguageJson)
                .V()
                .Verify(this);
        }

        [Fact]
        public async Task Tuple()
        {
            await _g
                .WithExecutor(TupleOfPersonLanguageJson)
                .V()
                .Cast<(Person, Language)>()
                .Verify(this);
        }

        [Fact]
        public async Task Tuple_vertex_vertex()
        {
            await _g
                .WithExecutor(TupleOfPersonLanguageJson)
                .V()
                .Cast<(Vertex, Vertex)>()
                .Verify(this);
        }

        [Fact]
        public async Task NamedTuple()
        {
            await _g
                .WithExecutor(NamedTupleOfPersonLanguageJson)
                .V()
                .Cast<PersonLanguageTuple>()
                .Verify(this);
        }

        [Fact]
        public async Task Graphson3_Tuple()
        {
            await _g
                .WithExecutor(Graphson3TupleOfPersonLanguageJson)
                .V()
                .Cast<(Person, Language)>()
                .Verify(this);
        }

        [Fact]
        public async Task Array()
        {
            await _g
                .WithExecutor(ArrayOfLanguages)
                .V()
                .Cast<Language[]>()
                .Verify(this);
        }

        [Fact]
        public async Task Nested_Array()
        {
            await _g
                .WithExecutor(NestedArrayOfLanguagesJson)
                .V()
                .Cast<Language[][]>()
                .Verify(this);
        }

        [Fact]
        public async Task Scalar()
        {
            await _g
                .WithExecutor("[ 36 ]")
                .V()
                .Cast<int>()
                .Verify(this);
        }

        [Fact]
        public async Task Meta_Properties()
        {
            await _g
                .WithExecutor(CountryWithMetaProperties)
                .V<Country>()
                .Verify(this);
        }

        [Fact]
        public async Task VertexProperties()
        {
            await _g
                .WithExecutor(GetJson("VertexProperties"))
                .V()
                .Properties()
                .Verify(this);
        }

        [Fact]
        public async Task VertexProperties_with_model()
        {
            await _g
                .WithExecutor(GetJson("VertexProperties"))
                .V()
                .Properties()
                .Meta<MetaPoco>()
                .Verify(this);
        }

        [Fact]
        public async Task MetaProperties()
        {
            await _g
                .WithExecutor(GetJson("Properties"))
                .V()
                .Properties()
                .Properties()
                .Verify(this);
        }

        [Fact]
        public async Task VertexPropertyWithoutProperties()
        {
            await _g
                .WithExecutor("[ { \"id\": 166, \"value\": \"bob\", \"label\": \"Name\" } ]")
                .V<Person>()
                .Properties(x => x.SomeObscureProperty)
                .Verify(this);
        }

        [Fact]
        public async Task VertexPropertyWithDateTimeOffset()
        {
            await _g
                .WithExecutor("[ { \"id\": 166, \"value\": \"bob\", \"label\": \"Name\", \"properties\": { \"ValidFrom\": 1548112365431 } } ]")
                .V<Person>()
                .Properties(x => x.Name)
                .Verify(this);
        }

        [Fact]
        public async Task PropertyWithDateTimeOffset()
        {
            await _g
                .WithExecutor("{ \"@type\": \"g:List\",\"@value\": [ { \"@type\": \"g:Property\", \"@value\": { \"key\": \"ValidFrom\", \"value\": { \"@type\": \"g:Date\", \"@value\": 1548169812555 } } } ] }")
                .V<Person>()
                .Properties(x => x.Name)
                .Properties(x => x.ValidFrom)
                .Verify(this);
        }

        [Fact]
        public async Task Traverser()
        {
            await _g
                .WithExecutor(ThreeCompaniesAsTraverser)
                .V<Company>()
                .Verify(this);
        }

        [Fact]
        public async Task Nullable()
        {
            await _g
                .WithExecutor("[ { \"Item1\": [],  \"Item2\": [], \"Item3\": \"someString\", \"Item4\": \"someString\", \"Item5\": [],  \"Item5\": null } ]")
                .V<(string, string?, string, string?, int?, int?)>()
                .Verify(this);
        }

        private static string GetJson(string name)
        {
            return new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream($"ExRam.Gremlinq.Core.Tests.Json.{name}.json")).ReadToEnd();
        }
    }
}
