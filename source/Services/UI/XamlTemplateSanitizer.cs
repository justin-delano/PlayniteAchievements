using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PlayniteAchievements.Services.UI
{
    /// <summary>
    /// Allowlist check for loose notification-template XAML before it is parsed by WPF. Loose
    /// XAML can instantiate any public type reachable from the process and call methods on it
    /// (<c>ObjectDataProvider</c>, arbitrary <c>clr-namespace</c> declarations, <c>x:Static</c>
    /// into foreign assemblies), so a template from another user is admitted only when every
    /// namespace, type, markup extension and URI it uses is one a toast or frame legitimately
    /// needs: WPF's presentation and <c>x</c> namespaces, this plugin's own types, a few
    /// <c>mscorlib</c> primitives, and <c>pack://</c> resources of this plugin.
    /// The same file runs inside the plugin and in the community repository's validator.
    /// </summary>
    public static class XamlTemplateSanitizer
    {
        public const int MaxTemplateBytes = 512 * 1024;

        public const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        public const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        public const string BlendNamespace = "http://schemas.microsoft.com/expression/blend/2008";
        public const string SystemNamespace = "clr-namespace:System;assembly=mscorlib";
        public const string PluginPackUriPrefix = "pack://application:,,,/PlayniteAchievements;component/";

        private static readonly Regex PluginNamespacePattern = new Regex(
            @"^clr-namespace:PlayniteAchievements(\.[A-Za-z_][A-Za-z0-9_]*)*;assembly=PlayniteAchievements$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Element local names (presentation namespace) that reach outside the visual tree.
        private static readonly HashSet<string> ForbiddenPresentationElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "ObjectDataProvider",
            "XmlDataProvider",
            "Frame",
            "WebBrowser",
            "MediaElement",
            "MediaPlayer",
            "NavigationWindow",
            "Window",
            "Page",
            "WindowsFormsHost",
            "Hyperlink"
        };

        // x: elements and attributes that compile code, pick constructors, or subclass.
        private static readonly HashSet<string> ForbiddenXamlDirectives = new HashSet<string>(StringComparer.Ordinal)
        {
            "Code",
            "XData",
            "Class",
            "Subclass",
            "ClassModifier",
            "FactoryMethod",
            "Arguments",
            "TypeArguments"
        };

        // mscorlib types a template can reasonably declare as resources.
        private static readonly HashSet<string> AllowedSystemTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "String",
            "Double",
            "Single",
            "Boolean",
            "Int32",
            "Int64",
            "Int16",
            "Byte",
            "Decimal",
            "Char",
            "TimeSpan",
            "DateTime"
        };

        // Attributes whose plain (non-binding) value is a location WPF will load from.
        private static readonly HashSet<string> UriAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Source",
            "UriSource",
            "ImageSource",
            "BaseUri",
            "NavigateUri",
            "Uri",
            "StreamSource",
            "FontUri"
        };

        // {p:Static prefix:Type.Member} and {p:Type prefix:Type}, in either positional or
        // Member=/TypeName= form, anywhere inside a markup extension string. The extension's own
        // prefix is captured rather than assumed to be "x", and resolved against the element's
        // in-scope namespaces, so aliasing the XAML namespace cannot slip a reference past.
        private static readonly Regex TypeReferencePattern = new Regex(
            @"\{\s*(?<pfx>[A-Za-z_]\w*):(?<ext>Static|Type)\s+(?:(?:Member|TypeName|Type)\s*=\s*)?(?<ref>[A-Za-z_]\w*(?::[A-Za-z_]\w*)?(?:\.[A-Za-z_]\w*)*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// True when the template passes every check; otherwise <paramref name="error"/> names the
        /// first offending construct so the author can fix it.
        /// </summary>
        public static bool TryValidate(string xaml, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(xaml))
            {
                error = "The template file is empty.";
                return false;
            }

            if (xaml.Length > MaxTemplateBytes)
            {
                error = $"The template is larger than {MaxTemplateBytes / 1024} KB.";
                return false;
            }

            XDocument document;
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreProcessingInstructions = true,
                    MaxCharactersFromEntities = 0
                };
                using (var stringReader = new StringReader(xaml))
                using (var reader = XmlReader.Create(stringReader, settings))
                {
                    document = XDocument.Load(reader, LoadOptions.None);
                }
            }
            catch (XmlException ex)
            {
                error = $"The template is not well-formed XML: {ex.Message}";
                return false;
            }

            if (document.Root == null)
            {
                error = "The template has no root element.";
                return false;
            }

            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (!CheckElement(element, out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CheckElement(XElement element, out string error)
        {
            error = null;

            foreach (var declaration in element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            {
                if (!IsAllowedNamespace(declaration.Value))
                {
                    error = $"The XML namespace '{declaration.Value}' is not allowed in a shared template.";
                    return false;
                }
            }

            var ns = element.Name.NamespaceName;
            var local = element.Name.LocalName;
            if (!IsAllowedNamespace(ns))
            {
                error = $"The element '{element.Name}' uses a namespace that is not allowed in a shared template.";
                return false;
            }

            if (ns == PresentationNamespace || ns.Length == 0)
            {
                // "Grid.Row"-style property elements carry the owner type before the dot.
                var typeName = local.Split('.')[0];
                if (ForbiddenPresentationElements.Contains(typeName))
                {
                    error = $"The element '{typeName}' is not allowed in a shared template.";
                    return false;
                }
            }
            else if (ns == XamlNamespace)
            {
                if (ForbiddenXamlDirectives.Contains(local))
                {
                    error = $"'x:{local}' is not allowed in a shared template.";
                    return false;
                }

                if (local == "Static" || local == "Type")
                {
                    var reference = (string)element.Attribute("Member")
                        ?? (string)element.Attribute("TypeName")
                        ?? (string)element.Attribute("Type")
                        ?? string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value)).Trim();
                    if (!CheckTypeReference(element, local, reference, out error))
                    {
                        return false;
                    }
                }
            }
            else if (ns == SystemNamespace)
            {
                if (!AllowedSystemTypes.Contains(local))
                {
                    error = $"The System type '{local}' is not allowed in a shared template.";
                    return false;
                }
            }

            foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                if (!CheckAttribute(element, attribute, out error))
                {
                    return false;
                }
            }

            foreach (var text in element.Nodes().OfType<XText>())
            {
                if (!CheckMarkupExtensions(element, text.Value, out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CheckAttribute(XElement element, XAttribute attribute, out string error)
        {
            error = null;
            var attributeNs = attribute.Name.NamespaceName;
            var attributeLocal = attribute.Name.LocalName;

            if (attributeNs == XamlNamespace && ForbiddenXamlDirectives.Contains(attributeLocal))
            {
                error = $"'x:{attributeLocal}' is not allowed in a shared template.";
                return false;
            }

            if (attributeNs.Length > 0 && !IsAllowedNamespace(attributeNs))
            {
                error = $"The attribute '{attribute.Name}' uses a namespace that is not allowed in a shared template.";
                return false;
            }

            var value = attribute.Value ?? string.Empty;
            if (!CheckMarkupExtensions(element, value, out error))
            {
                return false;
            }

            // Plain (non-extension) values on location attributes must point into this plugin.
            // Attached properties arrive as "Owner.Property", so judge the trailing part.
            var propertyName = attributeLocal.Substring(attributeLocal.LastIndexOf('.') + 1);
            if (UriAttributeNames.Contains(propertyName) && !value.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                if (!IsAllowedUri(value))
                {
                    error = $"The '{attributeLocal}' value '{value}' is not allowed in a shared template; only '{PluginPackUriPrefix}...' resources can be referenced.";
                    return false;
                }
            }

            return true;
        }

        private static bool CheckMarkupExtensions(XElement element, string value, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(value) || value.IndexOf('{') < 0)
            {
                return true;
            }

            foreach (Match match in TypeReferencePattern.Matches(value))
            {
                var extensionNs = element.GetNamespaceOfPrefix(match.Groups["pfx"].Value)?.NamespaceName;
                if (extensionNs != XamlNamespace)
                {
                    continue;
                }

                if (!CheckTypeReference(element, match.Groups["ext"].Value, match.Groups["ref"].Value, out error))
                {
                    return false;
                }
            }

            if (value.IndexOf("pack://siteoforigin", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                error = "'pack://siteoforigin' references are not allowed in a shared template.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves the prefix of a <c>prefix:Type.Member</c> reference against the element's
        /// in-scope namespaces and admits only presentation, plugin, or allowlisted System types.
        /// </summary>
        private static bool CheckTypeReference(XElement element, string extension, string reference, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(reference))
            {
                return true;
            }

            var colon = reference.IndexOf(':');
            var prefix = colon > 0 ? reference.Substring(0, colon) : string.Empty;
            var remainder = colon > 0 ? reference.Substring(colon + 1) : reference;
            var typeName = remainder.Split('.')[0];

            var ns = prefix.Length == 0
                ? element.GetDefaultNamespace().NamespaceName
                : element.GetNamespaceOfPrefix(prefix)?.NamespaceName;

            if (ns == null)
            {
                error = $"'x:{extension} {reference}' uses the undeclared prefix '{prefix}'.";
                return false;
            }

            if (ns == PresentationNamespace || ns.Length == 0 || PluginNamespacePattern.IsMatch(ns))
            {
                if (ForbiddenPresentationElements.Contains(typeName))
                {
                    error = $"'x:{extension} {reference}' is not allowed in a shared template.";
                    return false;
                }

                return true;
            }

            if (ns == SystemNamespace && AllowedSystemTypes.Contains(typeName))
            {
                return true;
            }

            error = $"'x:{extension} {reference}' refers to a type outside the plugin and is not allowed in a shared template.";
            return false;
        }

        private static bool IsAllowedNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns))
            {
                return true;
            }

            return ns == PresentationNamespace ||
                   ns == XamlNamespace ||
                   ns == MarkupCompatibilityNamespace ||
                   ns == BlendNamespace ||
                   ns == SystemNamespace ||
                   PluginNamespacePattern.IsMatch(ns);
        }

        private static bool IsAllowedUri(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }

            return trimmed.StartsWith(PluginPackUriPrefix, StringComparison.OrdinalIgnoreCase) &&
                   trimmed.IndexOf("..", StringComparison.Ordinal) < 0;
        }
    }
}
