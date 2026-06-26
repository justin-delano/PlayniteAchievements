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
        public void ResourceDescriptors_DoesNotExposeDerivedChromeBrushes()
        {
            AssertNoDescriptor("PlayAch.Brush.Grid.HeaderGripper.Hover");
            AssertNoDescriptor("PlayAch.Brush.Grid.HeaderGripper.Pressed");
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

        [TestMethod]
        public void Apply_DerivesHeaderGripperBrushesFromCustomAppearanceBrushes()
        {
            var resources = new ResourceDictionary();
            var overrides = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["PlayAch.Brush.Accent"] = CreateBrushOverride("#FF405060"),
                ["PlayAch.Brush.Selection"] = CreateBrushOverride("#FF708090"),
                ["PlayAch.Brush.Grid.HeaderGripper.Hover"] = CreateBrushOverride("#FFFFFFFF")
            };

            PlayAchResourceService.Apply(resources, overrides);

            AssertBrush(resources, "PlayAch.Brush.Grid.HeaderGripper.Hover", Color.FromRgb(0x40, 0x50, 0x60));
            AssertBrush(resources, "PlayAch.Brush.Grid.HeaderGripper.Pressed", Color.FromRgb(0x70, 0x80, 0x90));
        }

        [TestMethod]
        public void Apply_ResolvesTransparentOverrideToTransparentBrush()
        {
            var resources = new ResourceDictionary();
            var overrides = new Dictionary<string, ResourceOverrideSetting>(StringComparer.OrdinalIgnoreCase)
            {
                ["PlayAch.Brush.GridSurface"] = new ResourceOverrideSetting
                {
                    Mode = ResourceOverrideMode.Transparent,
                    CustomValue = PlayAchResourceService.TransparentValue
                }
            };

            PlayAchResourceService.Apply(resources, overrides);

            Assert.IsTrue(resources.Contains("PlayAch.Brush.GridSurface"));
            var brush = resources["PlayAch.Brush.GridSurface"] as SolidColorBrush;
            Assert.IsNotNull(brush);
            Assert.AreEqual((byte)0, brush.Color.A);
            Assert.IsTrue(brush.IsFrozen);
        }

        [TestMethod]
        public void CreateDefaultResourceOverrides_SeedsSurfacesAsTransparent()
        {
            var defaults = PersistedSettings.CreateDefaultResourceOverrides();

            foreach (var key in new[]
            {
                "PlayAch.Brush.GridSurface",
                "PlayAch.Brush.WindowSurface",
                "PlayAch.Brush.ControlSurface"
            })
            {
                Assert.IsTrue(defaults.ContainsKey(key), key);
                Assert.AreEqual(ResourceOverrideMode.Transparent, defaults[key].Mode, key);
                Assert.AreEqual(PlayAchResourceService.TransparentValue, defaults[key].CustomValue, key);
            }
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
