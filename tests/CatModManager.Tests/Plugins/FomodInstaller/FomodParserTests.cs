using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using NSubstitute;
using CatModManager.PluginSdk;
using CmmPlugin.FomodInstaller.Models;
using CmmPlugin.FomodInstaller.Parser;

namespace CatModManager.Tests.Plugins.FomodInstaller;

public class FomodParserTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dummyZip;

    public FomodParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FomodTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dummyZip = Path.Combine(_tempDir, "dummy.zip");
        File.WriteAllText(_dummyZip, "not a real zip but file must exist");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

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
        var mockExtractor = Substitute.For<IArchiveExtractor>();
        mockExtractor.GetFileList(Arg.Any<string>()).Returns(new[] { "fomod/ModuleConfig.xml" });
        
        bool isFomod = FomodParser.IsFomod(_dummyZip, mockExtractor);
        
        Assert.True(isFomod);
    }

    [Fact]
    public void Parse_Basic_ReturnsCorrectNameAndFiles()
    {
        var mockExtractor = Substitute.For<IArchiveExtractor>();
        string configPath = "fomod/ModuleConfig.xml";
        
        mockExtractor.GetFileList(Arg.Any<string>()).Returns(new[] { configPath });
        
        var xmlStream = new MemoryStream(Encoding.UTF8.GetBytes(BasicFomodXml));
        // Stubbing the batch call, because that is what the parser uses: reading anything out
        // of a solid archive costs a full decode, so the config and the previews come in one pass.
        mockExtractor.OpenFileStreams(Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Stream> { [configPath] = xmlStream });

        var config = FomodParser.Parse(_dummyZip, mockExtractor);

        Assert.Equal("Test Mod", config.ModuleName);
        Assert.Single(config.RequiredInstallFiles);
        Assert.Equal("Core/test.esp", config.RequiredInstallFiles[0].Source);
    }

    [Fact]
    public void Parse_WithWrapperFolder_DetectsPrefix()
    {
        var mockExtractor = Substitute.For<IArchiveExtractor>();
        string nestedConfig = "MyAwesomeMod/fomod/ModuleConfig.xml";
        
        mockExtractor.GetFileList(Arg.Any<string>()).Returns(new[] { nestedConfig });
        
        var xmlStream = new MemoryStream(Encoding.UTF8.GetBytes(BasicFomodXml));
        // Stubbing the batch call, because that is what the parser uses: reading anything out
        // of a solid archive costs a full decode, so the config and the previews come in one pass.
        mockExtractor.OpenFileStreams(Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Stream> { [nestedConfig] = xmlStream });

        var config = FomodParser.Parse(_dummyZip, mockExtractor);

        Assert.Equal("MyAwesomeMod/", config.WrapperPrefix);
    }

    /// <summary>
    /// "Jiggle Physics Standard Body and Outfits" (Starfield, Nexus 15608) wraps everything in a
    /// folder called <c>Jiggle_Fomod/</c>. Searching the key for the first "fomod/" found it inside
    /// the wrapper's own name, so the prefix came out as "Jiggle_" and every source pointed at a
    /// path that does not exist — the install produced an empty mod folder, silently.
    /// </summary>
    [Fact]
    public void Parse_WhenWrapperNameItselfEndsInFomod_StillDetectsTheWholeWrapper()
    {
        var mockExtractor = Substitute.For<IArchiveExtractor>();
        string nestedConfig = "Jiggle_Fomod/fomod/ModuleConfig.xml";

        mockExtractor.GetFileList(Arg.Any<string>()).Returns(new[] { nestedConfig });

        var xmlStream = new MemoryStream(Encoding.UTF8.GetBytes(BasicFomodXml));
        // Stubbing the batch call, because that is what the parser uses: reading anything out
        // of a solid archive costs a full decode, so the config and the previews come in one pass.
        mockExtractor.OpenFileStreams(Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Stream> { [nestedConfig] = xmlStream });

        var config = FomodParser.Parse(_dummyZip, mockExtractor);

        Assert.Equal("Jiggle_Fomod/", config.WrapperPrefix);
    }

    /// <summary>
    /// FOMOD Creation Tool writes the tests one level down, inside a &lt;dependencies&gt; element.
    /// Reading only the direct children of &lt;visible&gt; found none, and a condition with no tests
    /// means "always visible" — so in "MTM 3BBB CBP OCBP OCBPC Physics Preset" (Fallout 4, Nexus
    /// 39195) both the CBBE step and the Fusion Girl step were shown, and since the two write the
    /// same <c>cbp.ini</c>, the second one quietly overwrote the choice made in the first.
    /// </summary>
    [Fact]
    public void Parse_ReadsAVisibleCondition_WrittenInsideADependenciesElement()
    {
        const string Xml = @"
<config>
    <moduleName>MTM CBP Physics Preset</moduleName>
    <installSteps order=""Explicit"">
        <installStep name=""Select Body Type"">
            <optionalFileGroups>
                <group name=""Body Type"" type=""SelectExactlyOne"">
                    <plugins>
                        <plugin name=""CBBE""><conditionFlags><flag name=""CBBE"">On</flag></conditionFlags></plugin>
                        <plugin name=""Fusion Girl""><conditionFlags><flag name=""FusionGirl"">On</flag></conditionFlags></plugin>
                    </plugins>
                </group>
            </optionalFileGroups>
        </installStep>
        <installStep name=""MTM CBP Physics Fusion Girl"">
            <visible>
                <dependencies operator=""And"">
                    <flagDependency flag=""FusionGirl"" value=""On""/>
                </dependencies>
            </visible>
        </installStep>
    </installSteps>
</config>";

        var mockExtractor = Substitute.For<IArchiveExtractor>();
        const string configPath = "fomod/ModuleConfig.xml";

        mockExtractor.GetFileList(Arg.Any<string>()).Returns(new[] { configPath });
        mockExtractor.OpenFileStreams(Arg.Any<string>(), Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, Stream>
            {
                [configPath] = new MemoryStream(Encoding.UTF8.GetBytes(Xml))
            });

        var config = FomodParser.Parse(_dummyZip, mockExtractor);

        var condition = config.InstallSteps[1].VisibleWhen;
        Assert.NotNull(condition);
        Assert.Equal(new FomodFlagDependency("FusionGirl", "On"), Assert.Single(condition!.FlagDependencies));

        // And the whole point: with CBBE chosen, the Fusion Girl step is not walked through.
        Assert.False(condition.IsSatisfiedBy(new Dictionary<string, string> { ["CBBE"] = "On" }));
        Assert.True(condition.IsSatisfiedBy(new Dictionary<string, string> { ["FusionGirl"] = "On" }));
    }
}
