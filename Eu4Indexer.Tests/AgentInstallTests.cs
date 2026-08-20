using Eu4Indexer.Core;

namespace Eu4Indexer.Tests;

public class AgentInstallTests
{
    [Fact]
    public void InstallCodex_RegistersMcpAndCopiesSelectedLanguageSkills()
    {
        var root = Path.Combine(Path.GetTempPath(), $"eu4_agent_install_{Guid.NewGuid():N}");
        var userHome = Path.Combine(root, "home");
        var skillSource = Path.Combine(root, "bundled-skills");

        try
        {
            WriteSkill(skillSource, "eu4", "en", "eu4 english");
            WriteSkill(skillSource, "eu4", "zh", "eu4 chinese");
            WriteSkill(skillSource, "hoi4", "zh", "hoi4 chinese");

            var nestedSource = Path.Combine(skillSource, "hoi4", "zh", "references");
            Directory.CreateDirectory(nestedSource);
            File.WriteAllText(Path.Combine(nestedSource, "workflow.md"), "nested resource");

            var codexConfig = Path.Combine(userHome, ".codex", "config.toml");
            Directory.CreateDirectory(Path.GetDirectoryName(codexConfig)!);
            File.WriteAllText(codexConfig, "model = \"test-model\"\n\n[mcp_servers.other]\ncommand = \"other\"\n");

            var first = AgentInstall.installCodexAt(userHome, skillSource, "zh");
            var second = AgentInstall.installCodexAt(userHome, skillSource, "zh");

            Assert.True(first.Ok, first.Message);
            Assert.True(second.Ok, second.Message);
            Assert.Contains("skill (zh)", second.Message);
            Assert.Contains(".codex/skills", second.Message);

            var config = File.ReadAllText(codexConfig);
            Assert.Contains("model = \"test-model\"", config);
            Assert.Contains("[mcp_servers.other]", config);
            Assert.Contains("[mcp_servers.eu4]", config);
            var escapedExecutable = AgentInstall.exePath().Replace("\\", "\\\\").Replace("\"", "\\\"");
            Assert.Contains($"command = \"{escapedExecutable}\"", config);
            Assert.Contains("args = [\"serve\"]", config);
            Assert.Equal(1, CountOccurrences(config, "[mcp_servers.eu4]"));
            Assert.True(File.Exists(codexConfig + ".bak"));

            var skillsHome = Path.Combine(userHome, ".codex", "skills");
            Assert.Equal("eu4 chinese", File.ReadAllText(Path.Combine(skillsHome, "eu4", "SKILL.md")));
            Assert.Equal("hoi4 chinese", File.ReadAllText(Path.Combine(skillsHome, "hoi4", "SKILL.md")));
            Assert.False(Directory.Exists(Path.Combine(userHome, ".agents", "skills")));
            Assert.Equal(
                "nested resource",
                File.ReadAllText(Path.Combine(skillsHome, "hoi4", "references", "workflow.md")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }

    private static void WriteSkill(string source, string game, string language, string contents)
    {
        var directory = Path.Combine(source, game, language);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), contents);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;

        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
