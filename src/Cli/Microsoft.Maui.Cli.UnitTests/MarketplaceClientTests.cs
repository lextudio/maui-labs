// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class MarketplaceClientTests
{
	[Fact]
	public void ParseFrontmatter_ValidFrontmatter_ExtractsNameAndDescription()
	{
		var content = """
			---
			name: test-skill
			description: A test skill for MAUI development
			---
			# Test Skill
			Some content here.
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("test-skill", name);
		Assert.Equal("A test skill for MAUI development", description);
	}

	[Fact]
	public void ParseFrontmatter_QuotedValues_StripsQuotes()
	{
		var content = """
			---
			name: "quoted-skill"
			description: 'A quoted description'
			---
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("quoted-skill", name);
		Assert.Equal("A quoted description", description);
	}

	[Fact]
	public void ParseFrontmatter_NoFrontmatter_ReturnsNulls()
	{
		var content = """
			# Just a markdown file
			No frontmatter here.
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Null(name);
		Assert.Null(description);
	}

	[Fact]
	public void ParseFrontmatter_EmptyContent_ReturnsNulls()
	{
		var (name, description) = MarketplaceClient.ParseFrontmatter("");

		Assert.Null(name);
		Assert.Null(description);
	}

	[Fact]
	public void ParseFrontmatter_NoClosingDelimiter_ReturnsNulls()
	{
		var content = """
			---
			name: incomplete
			description: No closing delimiter
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Null(name);
		Assert.Null(description);
	}

	[Fact]
	public void ParseFrontmatter_OnlyNamePresent_ReturnsNameWithNullDescription()
	{
		var content = """
			---
			name: name-only
			---
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("name-only", name);
		Assert.Null(description);
	}

	[Fact]
	public void ParseFrontmatter_OnlyDescriptionPresent_ReturnsDescriptionWithNullName()
	{
		var content = """
			---
			description: description-only
			---
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Null(name);
		Assert.Equal("description-only", description);
	}

	[Fact]
	public void ParseFrontmatter_LeadingWhitespace_StillParses()
	{
		var content = "   \n\n---\nname: whitespace-skill\ndescription: Has leading whitespace\n---\n";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("whitespace-skill", name);
		Assert.Equal("Has leading whitespace", description);
	}

	[Fact]
	public void ParseFrontmatter_ExtraFieldsIgnored()
	{
		var content = """
			---
			name: my-skill
			version: 1.0
			author: test
			description: A skill with extra fields
			tags: [a, b, c]
			---
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("my-skill", name);
		Assert.Equal("A skill with extra fields", description);
	}

	[Fact]
	public void ParseFrontmatter_CaseInsensitiveKeys()
	{
		var content = """
			---
			Name: CasedSkill
			Description: Case insensitive keys
			---
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("CasedSkill", name);
		Assert.Equal("Case insensitive keys", description);
	}

	[Theory]
	[InlineData("---\nname: a\ndescription: b\n---", "a", "b")]
	[InlineData("---\nname:   spaced  \ndescription:   also spaced  \n---", "spaced", "also spaced")]
	[InlineData("---\nname: \"double-quoted\"\ndescription: 'single-quoted'\n---", "double-quoted", "single-quoted")]
	public void ParseFrontmatter_VariousFormats(string content, string expectedName, string expectedDescription)
	{
		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal(expectedName, name);
		Assert.Equal(expectedDescription, description);
	}

	[Fact]
	public void ParseFrontmatter_BlockScalarIndicator_TreatsAsPlainText()
	{
		// The >- YAML block scalar indicator is treated as a plain value
		// since ParseFrontmatter uses simple string operations
		var content = """
			---
			name: block-skill
			description: >-
			---
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("block-skill", name);
		// The >- is treated as the value since the parser uses simple line-by-line parsing
		Assert.NotNull(description);
	}

	[Fact]
	public void ParseFrontmatter_ContentAfterFrontmatter_IsIgnored()
	{
		var content = """
			---
			name: frontmatter-skill
			description: Only frontmatter matters
			---
			# Heading
			name: this-should-not-be-parsed
			description: Neither should this
			""";

		var (name, description) = MarketplaceClient.ParseFrontmatter(content);

		Assert.Equal("frontmatter-skill", name);
		Assert.Equal("Only frontmatter matters", description);
	}
}
