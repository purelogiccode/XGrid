using System;
using System.IO;
using XGrid.Frontend.Avalonia.Services;
using Xunit;

namespace XGrid.Frontend.Tests;

/// <summary>Pins catalog validation, lookup and URI resolution.</summary>
public sealed class CoverCatalogTests
{
    private static readonly string Id = new('a', 64);

    [Fact]
    public void LoadJson_IndexesTitleAliasesAndGameIds()
    {
        CoverCatalogLoadResult result = CoverCatalogIndex.LoadJson(CatalogJson(
            $$"""
              {
                "schemaVersion": 1,
                "packVersion": "test",
                "imagesBase": "images/",
                "covers": [
                  {
                    "id": "{{Id}}",
                    "file": "{{Id}}.webp",
                    "name": "Halo - Combat Evolved",
                    "aliases": ["Halo (USA)"],
                    "gameIds": ["4D530002"],
                    "regions": ["USA"],
                    "year": "2001"
                  }
                ]
              }
              """));

        Assert.NotNull(result.Catalog);
        Assert.NotNull(result.Index);
        Assert.Null(result.Error);
        Assert.Equal("4D530002", result.Index.FindByGameId("4D530002")?.GameIds[0]);
        Assert.Equal(Id, result.Index.FindByTitle("Halo - Combat Evolved", null, 0.85, true, null, null)?.Entry.Id);
        Assert.Equal(Id, result.Index.FindByTitle("Halo", null, 0.85, true, null, null)?.Entry.Id);
    }

    [Fact]
    public void LoadJson_RejectsADuplicateId()
    {
        CoverCatalogLoadResult result = CoverCatalogIndex.LoadJson(CatalogJson(
            $$"""
              {
                "schemaVersion": 1,
                "covers": [
                  { "id": "{{Id}}", "file": "{{Id}}.webp", "name": "A" },
                  { "id": "{{Id}}", "file": "{{Id}}.webp", "name": "B" }
                ]
              }
              """));

        Assert.Null(result.Index);
        Assert.Contains("more than once", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadJson_RejectsAFileNameThatDoesNotMatchTheId()
    {
        CoverCatalogLoadResult result = CoverCatalogIndex.LoadJson(CatalogJson(
            $$"""
              {
                "schemaVersion": 1,
                "covers": [
                  { "id": "{{Id}}", "file": "other.webp", "name": "A" }
                ]
              }
              """));

        Assert.Null(result.Index);
        Assert.Contains("does not match", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadJson_RejectsAnUnsupportedSchema()
    {
        CoverCatalogLoadResult result = CoverCatalogIndex.LoadJson(CatalogJson(
            $$"""
              {
                "schemaVersion": 99,
                "covers": [
                  { "id": "{{Id}}", "file": "{{Id}}.webp", "name": "A" }
                ]
              }
              """));

        Assert.Null(result.Index);
        Assert.Contains("schema", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadJson_RejectsInvalidJson()
    {
        CoverCatalogLoadResult result = CoverCatalogIndex.LoadJson("{");

        Assert.Null(result.Index);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ResolveImageUri_UsesTheCatalogFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "xgrid-catalog-" + Guid.NewGuid().ToString("N"));
        string catalogPath = Path.Combine(root, "catalog.json");
        var catalog = new CoverCatalog();
        var entry = new CoverCatalogEntry
        {
            Id = Id,
            File = Id + ".webp",
        };

        Uri uri = CoverCatalogIndex.ResolveImageUri(new Uri(catalogPath), catalog, entry);

        Assert.True(uri.IsFile);
        Assert.Equal(Path.Combine(root, "images", Id + ".webp"), uri.LocalPath);
    }

    [Fact]
    public void ResolveRelativeUri_RejectsParentTraversal()
    {
        Assert.Throws<InvalidDataException>(() =>
            CoverCatalogIndex.ResolveRelativeUri(new Uri("https://example.test/v1/catalog.json"), "../x.webp"));
    }

    private static string CatalogJson(string body)
    {
        return body.Trim();
    }
}
