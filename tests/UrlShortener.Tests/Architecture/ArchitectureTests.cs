using FluentAssertions;
using NetArchTest.Rules;
using System.Reflection;
using System.Xml.Linq;
using UrlShortener.Api.Controllers;
using UrlShortener.Api.Data;
using Xunit;

namespace UrlShortener.Tests.Architecture;
public class ArchitectureTests
{
    [Fact]
    public void Controllers_should_not_depend_on_dbcontext()
    {
        var result = Types.InAssembly(typeof(UrlsController).Assembly)
            .That().ResideInNamespace("UrlShortener.Api.Controllers")
            .ShouldNot().HaveDependencyOn(typeof(AppDbContext).Namespace!)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Public_controller_and_service_methods_should_have_xml_docs()
    {
        var assembly = typeof(UrlsController).Assembly;
        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        File.Exists(xmlPath).Should().BeTrue("documentation file must be generated");

        var xml = XDocument.Load(xmlPath);
        var members = xml.Root?.Element("members")?.Elements("member").Select(x => x.Attribute("name")?.Value).ToHashSet() ?? [];

        var publicMethods = assembly.GetTypes()
            .Where(t => t.IsPublic && (t.Namespace?.Contains("Controllers") == true || t.Namespace?.Contains("Services") == true))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName);

        foreach (var method in publicMethods)
        {
            var methodId = $"M:{method.DeclaringType!.FullName}.{method.Name}";
            members.Any(m => m != null && m.StartsWith(methodId, StringComparison.Ordinal)).Should().BeTrue($"Missing XML docs for {methodId}");
        }
    }
}
