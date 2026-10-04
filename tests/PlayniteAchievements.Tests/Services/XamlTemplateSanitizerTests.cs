using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Services.Tests
{
    [TestClass]
    public class XamlTemplateSanitizerTests
    {
        private const string Head =
            "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
            "xmlns:sys=\"clr-namespace:System;assembly=mscorlib\" " +
            "xmlns:vm=\"clr-namespace:PlayniteAchievements.ViewModels;assembly=PlayniteAchievements\" " +
            "xmlns:helpers=\"clr-namespace:PlayniteAchievements.Views.Helpers;assembly=PlayniteAchievements\"";

        [TestMethod]
        public void Accepts_PluginOnlyTemplate()
        {
            var xaml = Head + ">" +
                "<ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"pack://application:,,,/PlayniteAchievements;component/Resources/NotificationResources.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries>" +
                "<sys:Double x:Key=\"d\">3</sys:Double>" +
                "<DataTemplate x:Key=\"PlayAch.Template.AchievementToast\" DataType=\"{x:Type vm:AchievementToastViewModel}\">" +
                "<Grid><Image helpers:AsyncImage.Uri=\"{Binding ToastBackgroundRenderSource}\" Source=\"{Binding Icon}\"/>" +
                "<TextBlock Foreground=\"{x:Static SystemColors.ControlBrush}\" Text=\"{Binding Title}\"/></Grid>" +
                "</DataTemplate></ResourceDictionary>";

            Assert.IsTrue(XamlTemplateSanitizer.TryValidate(xaml, out var error), error);
        }

        [TestMethod]
        public void Rejects_ObjectDataProvider()
        {
            var xaml = Head + "><ObjectDataProvider x:Key=\"p\" ObjectType=\"{x:Type sys:String}\" MethodName=\"Copy\"/></ResourceDictionary>";
            AssertRejected(xaml, "ObjectDataProvider");
        }

        [TestMethod]
        public void Rejects_ForeignClrNamespace()
        {
            var xaml = Head + " xmlns:d=\"clr-namespace:System.Diagnostics;assembly=System\"><d:Process x:Key=\"p\"/></ResourceDictionary>";
            AssertRejected(xaml, "namespace");
        }

        [TestMethod]
        public void Rejects_PluginNamespaceWithoutAssembly()
        {
            // Loose XAML without assembly= resolves against the host, not the plugin; the shipped
            // default templates always qualify the assembly, so a shared template must too.
            var xaml = Head + " xmlns:bad=\"clr-namespace:PlayniteAchievements.Views.Helpers\"><bad:Thing x:Key=\"p\"/></ResourceDictionary>";
            AssertRejected(xaml, "namespace");
        }

        [TestMethod]
        public void Rejects_XCode()
        {
            var xaml = Head + "><x:Code><![CDATA[ int x; ]]></x:Code></ResourceDictionary>";
            AssertRejected(xaml, "x:Code");
        }

        [TestMethod]
        public void Rejects_XStaticIntoNonAllowlistedSystemType()
        {
            var xaml = Head + "><sys:String x:Key=\"s\">{x:Static sys:Environment.CommandLine}</sys:String></ResourceDictionary>";
            AssertRejected(xaml, "x:Static");
        }

        [TestMethod]
        public void Rejects_XStaticUnderAliasedXamlPrefix()
        {
            var xaml = Head.Replace("xmlns:x=", "xmlns:y=") +
                "><sys:String y:Key=\"s\">{y:Static sys:Environment.MachineName}</sys:String></ResourceDictionary>";
            AssertRejected(xaml, "Static");
        }

        [TestMethod]
        public void Rejects_ExternalImageSources()
        {
            AssertRejected(Head + "><DataTemplate x:Key=\"t\"><Image Source=\"https://example.invalid/a.png\"/></DataTemplate></ResourceDictionary>", "Source");
            AssertRejected(Head + "><DataTemplate x:Key=\"t\"><Image Source=\"C:\\Windows\\a.png\"/></DataTemplate></ResourceDictionary>", "Source");
            AssertRejected(Head + "><DataTemplate x:Key=\"t\"><Image Source=\"{Binding Source=pack://siteoforigin:,,,/a.png}\"/></DataTemplate></ResourceDictionary>", "siteoforigin");
        }

        [TestMethod]
        public void Rejects_MergedDictionaryFromAnotherAssembly()
        {
            var xaml = Head + "><ResourceDictionary.MergedDictionaries>" +
                "<ResourceDictionary Source=\"pack://application:,,,/Playnite;component/x.xaml\"/>" +
                "</ResourceDictionary.MergedDictionaries></ResourceDictionary>";
            AssertRejected(xaml, "Source");
        }

        [TestMethod]
        public void Rejects_Dtd()
        {
            var xaml = "<!DOCTYPE a [<!ENTITY e SYSTEM \"file:///c:/x\">]>" + Head + "><sys:String x:Key=\"s\">&e;</sys:String></ResourceDictionary>";
            AssertRejected(xaml, "XML");
        }

        [TestMethod]
        public void Rejects_EmptyAndMalformed()
        {
            AssertRejected(string.Empty, "empty");
            AssertRejected(Head + "><Grid></ResourceDictionary>", "XML");
        }

        private static void AssertRejected(string xaml, string expectedErrorFragment)
        {
            var accepted = XamlTemplateSanitizer.TryValidate(xaml, out var error);
            Assert.IsFalse(accepted, "Expected the template to be rejected.");
            StringAssert.Contains(error ?? string.Empty, expectedErrorFragment);
        }
    }
}
