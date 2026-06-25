using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Models.Settings;
using PlayniteAchievements.Services.UI;

namespace PlayniteAchievements.Tests.Services.UI
{
    [TestClass]
    public class PlayAchResourceServiceTests
    {
        [TestMethod]
        public void ResourceDescriptors_DoesNotExposeScrollbarBrushes()
        {
            AssertNoDescriptor("PlayAch.Brush.ScrollBar.Track");
            AssertNoDescriptor("PlayAch.Brush.ScrollBar.Thumb");
            AssertNoDescriptor("PlayAch.Brush.ScrollBar.Thumb.Hover");
            AssertNoDescriptor("PlayAch.Brush.ScrollBar.Thumb.Pressed");
        }

        [TestMethod]
        public void Apply_DerivesScrollbarBrushesFromCustomAppearanceBrushes()
        {
            var resources = new ResourceDictionary();
            var overrides = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["PlayAch.Brush.Surface"] = CreateBrushOverride("#FF101820"),
                ["PlayAch.Brush.ControlBorder"] = CreateBrushOverride("#FF334455"),
                ["PlayAch.Brush.Glyph"] = CreateBrushOverride("#FF556677"),
                ["PlayAch.Brush.Accent"] = CreateBrushOverride("#FF778899"),
                ["PlayAch.Brush.ScrollBar.Thumb"] = CreateBrushOverride("#FFFFFFFF")
            };

            PlayAchResourceService.Apply(resources, overrides);

            AssertBrush(resources, "PlayAch.Brush.ScrollBar.Track", Color.FromRgb(0x10, 0x18, 0x20));
            AssertBrush(resources, "PlayAch.Brush.ScrollBar.Thumb", Color.FromRgb(0x33, 0x44, 0x55));
            AssertBrush(resources, "PlayAch.Brush.ScrollBar.Thumb.Hover", Color.FromRgb(0x55, 0x66, 0x77));
            AssertBrush(resources, "PlayAch.Brush.ScrollBar.Thumb.Pressed", Color.FromRgb(0x77, 0x88, 0x99));
        }

        private static void AssertNoDescriptor(string resourceKey)
        {
            Assert.IsFalse(
                PlayAchResourceService.ResourceDescriptors.Any(item =>
                    string.Equals(item.ResourceKey, resourceKey, StringComparison.Ordinal)),
                resourceKey);
        }

        private static ResourceOverrideSetting CreateBrushOverride(string color)
        {
            return new ResourceOverrideSetting
            {
                Mode = ResourceOverrideMode.Custom,
                CustomValue = color
            };
        }

        private static void AssertBrush(ResourceDictionary resources, string resourceKey, Color expected)
        {
            Assert.IsTrue(resources.Contains(resourceKey), resourceKey);
            var brush = resources[resourceKey] as SolidColorBrush;
            Assert.IsNotNull(brush, resourceKey);
            Assert.AreEqual(expected, brush.Color);
            Assert.IsTrue(brush.IsFrozen, resourceKey);
        }
    }
}
