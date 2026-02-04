using System;
using System.Windows;

namespace PlayniteAchievements.Views.ThemeIntegration.Base
{
    /// <summary>
    /// Helper methods for registering dependency properties with reduced boilerplate.
    /// Use for simple property-based controls that primarily expose configuration options.
    /// </summary>
    public static class DependencyPropertyHelper
    {
        /// <summary>
        /// Registers a boolean dependency property for a control.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="ownerType">The type of the control that owns the property.</param>
        /// <param name="defaultValue">The default value for the property.</param>
        /// <returns>A registered DependencyProperty.</returns>
        public static DependencyProperty RegisterBoolProperty(
            string name,
            Type ownerType,
            bool defaultValue = false)
        {
            return DependencyProperty.Register(
                name,
                typeof(bool),
                ownerType,
                new FrameworkPropertyMetadata(defaultValue));
        }

        /// <summary>
        /// Registers an integer dependency property for a control.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="ownerType">The type of the control that owns the property.</param>
        /// <param name="defaultValue">The default value for the property.</param>
        /// <returns>A registered DependencyProperty.</returns>
        public static DependencyProperty RegisterIntProperty(
            string name,
            Type ownerType,
            int defaultValue = 0)
        {
            return DependencyProperty.Register(
                name,
                typeof(int),
                ownerType,
                new FrameworkPropertyMetadata(defaultValue));
        }

        /// <summary>
        /// Registers a double dependency property for a control.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="ownerType">The type of the control that owns the property.</param>
        /// <param name="defaultValue">The default value for the property.</param>
        /// <returns>A registered DependencyProperty.</returns>
        public static DependencyProperty RegisterDoubleProperty(
            string name,
            Type ownerType,
            double defaultValue = 0.0)
        {
            return DependencyProperty.Register(
                name,
                typeof(double),
                ownerType,
                new FrameworkPropertyMetadata(defaultValue));
        }

        /// <summary>
        /// Registers a string dependency property for a control.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="ownerType">The type of the control that owns the property.</param>
        /// <param name="defaultValue">The default value for the property.</param>
        /// <returns>A registered DependencyProperty.</returns>
        public static DependencyProperty RegisterStringProperty(
            string name,
            Type ownerType,
            string defaultValue = null)
        {
            return DependencyProperty.Register(
                name,
                typeof(string),
                ownerType,
                new FrameworkPropertyMetadata(defaultValue));
        }
    }
}
