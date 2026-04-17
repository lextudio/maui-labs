// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Commands;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AiCommandsTests
{
	[Fact]
	public void Create_ReturnsCommandNamedAi()
	{
		var command = AiCommands.Create();

		Assert.NotNull(command);
		Assert.Equal("ai", command.Name);
	}

	[Fact]
	public void Create_HasFiveSubcommands()
	{
		var command = AiCommands.Create();

		Assert.Equal(5, command.Subcommands.Count);
	}

	[Theory]
	[InlineData("init")]
	[InlineData("list")]
	[InlineData("status")]
	[InlineData("update")]
	[InlineData("add")]
	public void Create_ContainsExpectedSubcommand(string subcommandName)
	{
		var command = AiCommands.Create();

		Assert.Contains(command.Subcommands, c => c.Name == subcommandName);
	}

	[Fact]
	public void InitCommand_HasExpectedOptions()
	{
		var command = AiCommands.Create();
		var init = Assert.Single(command.Subcommands, c => c.Name == "init");

		Assert.Contains(init.Options, o => o.Name == "--repo");
		Assert.Contains(init.Options, o => o.Name == "--branch");
		Assert.Contains(init.Options, o => o.Name == "--force");
		Assert.Contains(init.Options, o => o.Name == "--no-mcp");
		Assert.Contains(init.Options, o => o.Name == "--skill");
		Assert.Contains(init.Options, o => o.Name == "--env");
	}

	[Fact]
	public void ListCommand_HasRepoAndBranchOptions()
	{
		var command = AiCommands.Create();
		var list = Assert.Single(command.Subcommands, c => c.Name == "list");

		Assert.Contains(list.Options, o => o.Name == "--repo");
		Assert.Contains(list.Options, o => o.Name == "--branch");
	}

	[Fact]
	public void StatusCommand_HasCheckUpdatesOption()
	{
		var command = AiCommands.Create();
		var status = Assert.Single(command.Subcommands, c => c.Name == "status");

		Assert.Contains(status.Options, o => o.Name == "--repo");
		Assert.Contains(status.Options, o => o.Name == "--branch");
		Assert.Contains(status.Options, o => o.Name == "--check-updates");
	}

	[Fact]
	public void UpdateCommand_HasExpectedOptions()
	{
		var command = AiCommands.Create();
		var update = Assert.Single(command.Subcommands, c => c.Name == "update");

		Assert.Contains(update.Options, o => o.Name == "--repo");
		Assert.Contains(update.Options, o => o.Name == "--branch");
		Assert.Contains(update.Options, o => o.Name == "--force");
		Assert.Contains(update.Options, o => o.Name == "--skill");
	}

	[Fact]
	public void AddCommand_HasRequiredSkillArgument()
	{
		var command = AiCommands.Create();
		var add = Assert.Single(command.Subcommands, c => c.Name == "add");

		var skillArg = Assert.Single(add.Arguments);
		Assert.Equal("skill", skillArg.Name);
	}

	[Fact]
	public void AddCommand_HasExpectedOptions()
	{
		var command = AiCommands.Create();
		var add = Assert.Single(command.Subcommands, c => c.Name == "add");

		Assert.Contains(add.Options, o => o.Name == "--repo");
		Assert.Contains(add.Options, o => o.Name == "--branch");
		Assert.Contains(add.Options, o => o.Name == "--force");
		Assert.Contains(add.Options, o => o.Name == "--no-mcp");
		Assert.Contains(add.Options, o => o.Name == "--env");
	}

	[Fact]
	public void BranchOption_HasShortAlias()
	{
		var command = AiCommands.Create();
		var init = Assert.Single(command.Subcommands, c => c.Name == "init");
		var branch = Assert.Single(init.Options, o => o.Name == "--branch");

		Assert.Contains("-b", branch.Aliases);
	}

	[Fact]
	public void ForceOption_HasShortAlias()
	{
		var command = AiCommands.Create();
		var init = Assert.Single(command.Subcommands, c => c.Name == "init");
		var force = Assert.Single(init.Options, o => o.Name == "--force");

		Assert.Contains("-y", force.Aliases);
	}

	[Fact]
	public void AiCommand_AllOptionsHaveValidAliases()
	{
		var command = AiCommands.Create();

		AssertNoWhitespaceAliases(command);
	}

	[Fact]
	public void BuildRootCommand_IncludesAiSubcommand()
	{
		var rootCommand = Program.BuildRootCommand();

		Assert.Contains(rootCommand.Subcommands, c => c.Name == "ai");
	}

	private static void AssertNoWhitespaceAliases(Command command)
	{
		foreach (var option in command.Options)
		{
			Assert.False(
				option.Name.Any(char.IsWhiteSpace),
				$"Option name contains whitespace: \"{option.Name}\" in command '{command.Name}'");

			foreach (var alias in option.Aliases)
			{
				Assert.False(
					alias.Any(char.IsWhiteSpace),
					$"Option alias contains whitespace: \"{alias}\" in command '{command.Name}'");
			}
		}

		foreach (var subcommand in command.Subcommands)
		{
			AssertNoWhitespaceAliases(subcommand);
		}
	}
}
