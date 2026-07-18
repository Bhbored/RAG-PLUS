using HtmlAgilityPack;
using Processor.Models;

namespace Processor.Services;

public class HtmlCleaner
{
    public ScrapedContent Extract(string url, HtmlDocument doc)
    {
        var content = new ScrapedContent();

        content.Title = doc.DocumentNode
            .SelectSingleNode("//title")?.InnerText.Trim() ?? url;

        var body = doc.DocumentNode.SelectSingleNode("//body");
        if (body is null)
        {
            content.BodyText = doc.DocumentNode.InnerText;
            return content;
        }

        // Remove boilerplate elements
        var boilerplate = body.SelectNodes(
            "//script|//style|//noscript|//nav|//footer|//header|//aside|//iframe|//button");
        if (boilerplate is not null)
            foreach (var node in boilerplate)
                node.Remove();

        // Extract headings
        var headingNodes = body.SelectNodes("//h1|//h2|//h3");
        if (headingNodes is not null)
            foreach (var h in headingNodes)
                if (!string.IsNullOrWhiteSpace(h.InnerText))
                    content.Headings.Add(h.InnerText.Trim());

        // Extract tables
        var tableNodes = body.SelectNodes("//table");
        if (tableNodes is not null)
        {
            foreach (var table in tableNodes)
            {
                var td = new TableData();
                var caption = table.SelectSingleNode(".//caption")?.InnerText.Trim();
                if (!string.IsNullOrEmpty(caption))
                    td.Caption = caption;

                var rows = table.SelectNodes(".//tr");
                if (rows is not null)
                {
                    foreach (var row in rows.Take(50))
                    {
                        var cells = row.SelectNodes(".//td|.//th");
                        td.Rows.Add(cells is not null
                            ? cells.Select(c => c.InnerText.Trim()).ToList()
                            : new List<string>());
                    }
                }
                content.Tables.Add(td);
            }
        }

        // Extract links (absolute URLs only)
        var linkNodes = body.SelectNodes("//a[@href]");
        if (linkNodes is not null)
        {
            foreach (var link in linkNodes.Take(100))
            {
                var href = link.GetAttributeValue("href", "");
                if (string.IsNullOrEmpty(href)) continue;
                if (href.StartsWith("http"))
                    content.Links.Add(href);
                else if (Uri.TryCreate(new Uri(url), href, out var absolute))
                    content.Links.Add(absolute.ToString());
            }
        }

        // Extract body text (cleaned)
        var textNodes = body.SelectNodes("//p|//li|//td|//span|//div");
        var bodyParts = new List<string>();
        if (textNodes is not null)
        {
            foreach (var node in textNodes)
            {
                var text = node.InnerText.Trim();
                if (!string.IsNullOrWhiteSpace(text) && text.Length > 20)
                    bodyParts.Add(text);
            }
        }

        if (bodyParts.Count == 0)
        {
            var allText = body.InnerText.Trim();
            bodyParts.Add(allText.Length > 10 ? allText : doc.DocumentNode.InnerText);
        }

        content.BodyText = string.Join("\n\n", bodyParts.Take(200));

        // Try to extract publish date from meta tags
        var dateMeta = doc.DocumentNode.SelectSingleNode(
            "//meta[@property='article:published_time']")
            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='date']")
            ?? doc.DocumentNode.SelectSingleNode("//meta[@name='pubdate']");

        if (dateMeta is not null)
        {
            var dateStr = dateMeta.GetAttributeValue("content", "");
            if (DateTime.TryParse(dateStr, out var date))
                content.PublishDate = date;
        }

        return content;
    }
}
