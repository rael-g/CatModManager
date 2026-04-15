using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using CatModManager.PluginSdk;
using CmmPlugin.FomodInstaller.Parser;

namespace CatModManager.Tests;

public class FomodParserTests
{
    private const string BasicFomodXml = @"
<config xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:noNamespaceSchemaLocation=""http://lite-mode.com/fomod/1.0/config.xsd"">
    <moduleName>Test Mod</moduleName>
    <requiredInstallFiles>
        <file source=""Core/test.esp"" destination=""test.esp""/>
    </requiredInstallFiles>
</config>";

    [Fact]
    public void IsFomod_ReturnsTrue_IfXmlExists()
    {
        var mockExtractor = new MockArchiveExtractor();
        mockExtractor.FileList.Add("fomod/ModuleConfig.xml");
        
        bool isFomod = FomodParser.IsFomod("dummy.zip", mockExtractor);
        
        Assert.True(isFomod);
    }

    [Fact]
    public void Parse_Basic_ReturnsCorrectNameAndFiles()
    {
        var mockExtractor = new MockArchiveExtractor();
        mockExtractor.FileList.Add("fomod/ModuleConfig.xml");
        mockExtractor.FileStreams["fomod/ModuleConfig.xml"] = new MemoryStream(Encoding.UTF8.GetBytes(BasicFomodXml));

        var config = FomodParser.Parse("dummy.zip", mockExtractor);

        Assert.Equal("Test Mod", config.ModuleName);
        Assert.Single(config.RequiredInstallFiles);
        Assert.Equal("Core/test.esp", config.RequiredInstallFiles[0].Source);
    }

    [Fact]
    public void Parse_WithWrapperFolder_DetectsPrefix()
    {
        var mockExtractor = new MockArchiveExtractor();
        // ModuleConfig is nested in a folder
        mockExtractor.FileList.Add("MyAwesomeMod/fomod/ModuleConfig.xml");
        mockExtractor.FileStreams["MyAwesomeMod/fomod/ModuleConfig.xml"] = new MemoryStream(Encoding.UTF8.GetBytes(BasicFomodXml));

        var config = FomodParser.Parse("dummy.zip", mockExtractor);

        Assert.Equal("MyAwesomeMod/", config.WrapperPrefix);
    }

    private class MockArchiveExtractor : IArchiveExtractor
    {
        public List<string> FileList { get; } = new();
        public Dictionary<string, MemoryStream> FileStreams { get; } = new();

        public Task ExtractAsync(string a, string d, IProgress<double>? p, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public IEnumerable<string> GetFileList(string a) => FileList;
        public Stream? OpenFileStream(string a, string entryPath)
        {
            if (FileStreams.TryGetValue(entryPath, out var ms))
            {
                var copy = new MemoryStream(ms.ToArray());
                return copy;
            }
            return null;
        }
    }
}
