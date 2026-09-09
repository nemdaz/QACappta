using System;
using System.Globalization;

namespace Qapptia.Core.Services;

/// <summary>
/// Helper para la construcción y extracción de paquetes de metadatos XMP (ISO 16684-1).
/// </summary>
public static class XmpMetadataHelper
{
    private const string XmpPacketHeader = "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n";
    private const string XmpPacketTrailer = "\n<?xpacket end=\"w\"?>";

    /// <summary>
    /// Construye el paquete estándar XMP en formato RDF/XML a partir de los metadatos de la imagen.
    /// </summary>
    public static string BuildXmpPacket(string mediaId, string mediaType, DateTime createdAt)
    {
        var localOffset = createdAt.Kind == DateTimeKind.Utc 
            ? new DateTimeOffset(createdAt.ToLocalTime()) 
            : new DateTimeOffset(createdAt);

        string dateStr = localOffset.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        string creatorTool = Constants.AppName.ToLowerInvariant();

        return $"{XmpPacketHeader}" +
               $"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
               $" <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
               $"  <rdf:Description rdf:about=\"\"\n" +
               $"    xmlns:xmpMM=\"{Constants.XmpNamespaceMediaManagement}\"\n" +
               $"    xmlns:dc=\"{Constants.XmpNamespaceDublinCore}\"\n" +
               $"    xmlns:xmp=\"{Constants.XmpNamespaceAdobeBasic}\">\n" +
               $"   <xmpMM:DocumentID>{mediaId}</xmpMM:DocumentID>\n" +
               $"   <dc:format>{mediaType}</dc:format>\n" +
               $"   <xmp:CreateDate>{dateStr}</xmp:CreateDate>\n" +
               $"   <xmp:CreatorTool>{creatorTool}</xmp:CreatorTool>\n" +
               $"  </rdf:Description>\n" +
               $" </rdf:RDF>\n" +
               $"</x:xmpmeta>" +
               $"{XmpPacketTrailer}";
    }

    /// <summary>
    /// Extrae los metadatos MediaId, MediaType y CreatedAt desde una cadena de texto XMP.
    /// </summary>
    public static (string? MediaId, string? MediaType, DateTime? CreatedAt) ParseXmpPacket(string xmpContent)
    {
        if (string.IsNullOrWhiteSpace(xmpContent))
            return (null, null, null);

        string? mediaId = ExtractElementOrAttribute(xmpContent, "xmpMM:DocumentID");
        if (string.IsNullOrEmpty(mediaId))
        {
            // Fallback a dc:identifier por interoperabilidad con otros visores
            mediaId = ExtractElementOrAttribute(xmpContent, "dc:identifier");
        }

        if (!string.IsNullOrEmpty(mediaId) && mediaId.StartsWith("uuid:", StringComparison.OrdinalIgnoreCase))
        {
            mediaId = mediaId[5..].Trim();
        }

        string? mediaType = ExtractElementOrAttribute(xmpContent, "dc:format");
        string? createDateStr = ExtractElementOrAttribute(xmpContent, "xmp:CreateDate");

        DateTime? createdAt = null;
        if (!string.IsNullOrWhiteSpace(createDateStr))
        {
            if (DateTimeOffset.TryParse(createDateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            {
                createdAt = dto.UtcDateTime;
            }
            else if (DateTime.TryParse(createDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                createdAt = dt.ToUniversalTime();
            }
        }

        return (mediaId, mediaType, createdAt);
    }

    private static string? ExtractElementOrAttribute(string xml, string tagName)
    {
        // 1. Intento por elemento XML: <tag> o <tag attr="...">
        string tagPrefix = $"<{tagName}";
        int searchPos = 0;
        while (searchPos < xml.Length)
        {
            int startIdx = xml.IndexOf(tagPrefix, searchPos, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0) break;

            int charAfter = startIdx + tagPrefix.Length;
            if (charAfter < xml.Length && (xml[charAfter] == '>' || char.IsWhiteSpace(xml[charAfter]) || xml[charAfter] == '/'))
            {
                if (xml[charAfter] == '/')
                {
                    searchPos = charAfter + 1;
                    continue;
                }

                int closeTagIdx = xml.IndexOf('>', charAfter);
                if (closeTagIdx > 0 && xml[closeTagIdx - 1] != '/')
                {
                    string endTag = $"</{tagName}>";
                    int endIdx = xml.IndexOf(endTag, closeTagIdx + 1, StringComparison.OrdinalIgnoreCase);
                    if (endIdx > closeTagIdx)
                    {
                        return xml[(closeTagIdx + 1)..endIdx].Trim();
                    }
                }
            }

            searchPos = startIdx + tagPrefix.Length;
        }

        // 2. Intento por atributo XML: tag="valor" o tag='valor'
        string attrKeyDoubleQuote = $"{tagName}=\"";
        int attrIdx = xml.IndexOf(attrKeyDoubleQuote, StringComparison.OrdinalIgnoreCase);
        if (attrIdx >= 0)
        {
            int valStart = attrIdx + attrKeyDoubleQuote.Length;
            int valEnd = xml.IndexOf('"', valStart);
            if (valEnd > valStart)
            {
                return xml[valStart..valEnd].Trim();
            }
        }

        string attrKeySingleQuote = $"{tagName}='";
        attrIdx = xml.IndexOf(attrKeySingleQuote, StringComparison.OrdinalIgnoreCase);
        if (attrIdx >= 0)
        {
            int valStart = attrIdx + attrKeySingleQuote.Length;
            int valEnd = xml.IndexOf('\'', valStart);
            if (valEnd > valStart)
            {
                return xml[valStart..valEnd].Trim();
            }
        }

        return null;
    }
}
