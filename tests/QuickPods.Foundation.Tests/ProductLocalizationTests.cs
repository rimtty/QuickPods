using System.Globalization;
using System.Text.RegularExpressions;
using QuickPods.Core.Models;
using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class ProductLocalizationTests
{
    [Theory]
    [InlineData("ja", ProductLanguage.Japanese)]
    [InlineData("ja-JP", ProductLanguage.Japanese)]
    [InlineData("en-US", ProductLanguage.English)]
    [InlineData("fr-FR", ProductLanguage.English)]
    public void SystemLanguageUsesJapaneseOnlyForJapaneseOperatingSystems(
        string cultureName,
        ProductLanguage expected)
    {
        ProductLanguage actual = ProductLanguageResolver.Resolve(
            QuickPodsLanguageMode.System,
            CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ExplicitLanguageOverridesTheOperatingSystemAndUnknownValuesFallBackToEnglish()
    {
        CultureInfo japanese = CultureInfo.GetCultureInfo("ja-JP");

        Assert.Equal(
            ProductLanguage.English,
            ProductLanguageResolver.Resolve(QuickPodsLanguageMode.English, japanese));
        Assert.Equal(
            ProductLanguage.Japanese,
            ProductLanguageResolver.Resolve(
                QuickPodsLanguageMode.Japanese,
                CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal(
            ProductLanguage.English,
            ProductLanguageResolver.Resolve((QuickPodsLanguageMode)99, japanese));
    }

    [Fact]
    public void EnglishAndJapaneseCatalogsHaveMatchingCompleteKeys()
    {
        var english = new ProductLocalizer(ProductLanguage.English);
        var japanese = new ProductLocalizer(ProductLanguage.Japanese);

        Assert.True(ProductLocalizer.HasMatchingLanguageKeys);
        Assert.Equal("Audio", english["AudioTitle"]);
        Assert.Equal("オーディオ", japanese["AudioTitle"]);
        Assert.All(ProductLocalizer.Keys, key => Assert.False(string.IsNullOrWhiteSpace(english[key])));
        Assert.All(ProductLocalizer.Keys, key => Assert.False(string.IsNullOrWhiteSpace(japanese[key])));
    }

    [Fact]
    public void EnglishAndJapaneseCatalogsUseMatchingFormatArguments()
    {
        var english = new ProductLocalizer(ProductLanguage.English);
        var japanese = new ProductLocalizer(ProductLanguage.Japanese);

        foreach (string key in ProductLocalizer.Keys)
        {
            string[] englishArguments = ExtractFormatArguments(english[key]);
            string[] japaneseArguments = ExtractFormatArguments(japanese[key]);

            Assert.Equal(englishArguments, japaneseArguments);
        }
    }

    [Fact]
    public void InvalidSavedLanguageNormalizesToSystem()
    {
        QuickPodsSettings normalized = (QuickPodsSettings.Default with
        {
            Language = (QuickPodsLanguageMode)99,
        }).Normalize();

        Assert.Equal(QuickPodsLanguageMode.System, QuickPodsSettings.Default.Language);
        Assert.Equal(QuickPodsLanguageMode.System, normalized.Language);
    }

    private static string[] ExtractFormatArguments(string value) =>
        Regex.Matches(value, @"\{(\d+)(?:[^{}]*)?\}")
            .Select(match => match.Groups[1].Value)
            .OrderBy(argument => argument, StringComparer.Ordinal)
            .ToArray();
}
