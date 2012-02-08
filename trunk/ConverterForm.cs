using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using System.Text.RegularExpressions;

namespace SplinderBloggerConverter
{
    public partial class ConverterForm : Form
    {
        private int postCount;
        private int commentCount;
        private int errorCount;
        private int fileCount;

        const string ATOM_NS = "http://www.w3.org/2005/Atom";
        const string PURL_NS = "http://purl.org/syndication/thread/1.0";

        public ConverterForm()
        {
            InitializeComponent();
        }

        private void onFileSelectClick(object sender, EventArgs e)
        {
            DialogResult result = openXmlFileDialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                filePathTextBox.Text = openXmlFileDialog.FileName;
            }
        }

        private void onConvertClick(object sender, EventArgs e)
        {
            statusTextBox.AppendText("Conversione in corso ...\r\n");
            readPosts(false);
            statusTextBox.AppendText("Conversione completata.\r\n");
        }

        private void onTestClick(object sender, EventArgs e)
        {
            statusTextBox.AppendText("Verifica in corso ...\r\n");
            readPosts(true);
            statusTextBox.AppendText("Verifica completata.\r\n");
        }

        private void readPosts(bool dryRun)
        {
            statusTextBox.AppendText("Analisi file XML in corso ...\r\n");

            postCount = 0;
            commentCount = 0;
            errorCount = 0;
            fileCount = 1;

            XmlWriter xw = null;
            if (!dryRun)
            {
                xw = startXmlArchive(fileCount);
            }

            XmlTextReader xtrInput = new XmlTextReader(new StreamReader(filePathTextBox.Text, Encoding.UTF8));
            while (xtrInput.Read())
            {
                while (xtrInput.NodeType == XmlNodeType.Element
                    && (xtrInput.Name.ToLower() == "entry"
                        || xtrInput.Name.ToLower() == "title"
                        || xtrInput.Name.ToLower() == "link"))
                {
                    try
                    {
                        String content = xtrInput.ReadOuterXml();
                        XmlDocument xdItem = new XmlDocument();
                        xdItem.LoadXml(content);

                        foreach (XmlNode node in xdItem.ChildNodes)
                        {
                            if (node.Name.ToLower() == "entry")
                            {
                                postCount++;
                                addPost(xw, postCount, node, dryRun);

                                if ((postCount + commentCount) > (fileCount * 1500))
                                {
                                    fileCount++;
                                    if (!dryRun)
                                    {
                                        endXmlArchive(xw);
                                        xw = startXmlArchive(fileCount);
                                    }
                                }
                            }
                            else if (node.Name.ToLower() == "title")
                            {
                                String title = getStringValue(xdItem, "title");
                                if (!dryRun)
                                {
                                    xw.WriteStartElement("title", ATOM_NS);
                                    xw.WriteAttributeString("type", "html");
                                    xw.WriteCData(title);
                                    xw.WriteEndElement();
                                }
                            }
                            else if (node.Name.ToLower() == "link")
                            {
                                if (!dryRun) xw.WriteStartElement("link", ATOM_NS);
                                foreach (XmlAttribute attribute in node.Attributes)
                                {
                                    if (!dryRun) xw.WriteAttributeString(attribute.Name, attribute.Value);
                                }
                                if (!dryRun) xw.WriteEndElement();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        statusTextBox.AppendText(ex.Message + "\r\n");
                        statusTextBox.AppendText(String.Format("Errore durante l'elaborazione del post {0}.\r\n", postCount));
                    }
                }
            }

            if (!dryRun)
            {
                try
                {
                    endXmlArchive(xw);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    statusTextBox.AppendText(ex.Message + "\r\n");
                    statusTextBox.AppendText(String.Format("Errore durante la conversione XML.\r\n"));
                }
            }

            statusTextBox.AppendText(String.Format("{0} post elaborati.\r\n", postCount));
            statusTextBox.AppendText(String.Format("{0} commenti elaborati.\r\n", commentCount));
            statusTextBox.AppendText(String.Format("{0} file elaborati.\r\n", fileCount));
        }

        private void endXmlArchive(XmlWriter xw)
        {
            xw.WriteEndElement();
            xw.Close();
        }

        private XmlWriter startXmlArchive(int fileCount)
        {
            String fileName = Path.GetFileName(filePathTextBox.Text);
            String filePath = Path.GetDirectoryName(filePathTextBox.Text);
            String prefix = String.Format("Blogger_{0}_", fileCount);
            String outFileName = Path.Combine(filePath, prefix + fileName);

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            XmlWriter xw = XmlWriter.Create(outFileName, settings);
            xw.WriteStartElement("ns0", "feed", ATOM_NS);
            xw.WriteElementString("generator", ATOM_NS, "Blogger");
            return xw;
        }

        private void addPost(XmlWriter xw, int postNumber, XmlNode entryNode, bool dryRun)
        {
            try {
                String title = getStringValue(entryNode, "title");
                String content = getStringValue(entryNode, "content");
                bool isDraft = getIntValue(entryNode, "status") != 1;
                DateTime published = getDateValue(entryNode, "published");
                DateTime updated = getDateValue(entryNode, "updated");
                String author = getAuthor(entryNode);
                String link = getLink(entryNode);

                if (!dryRun)
                {
                    insertPost(xw, postNumber, title, content, isDraft, published, updated, author, link, entryNode);
                }

                int commentNumber = 0;
                foreach (XmlNode node in entryNode.ChildNodes)
                {
                    if (node.Name == "comment")
                    {
                        commentNumber++;
                        commentCount++;
                        addComment(xw, postNumber, link, commentNumber, node, dryRun);
                    }
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                statusTextBox.AppendText(ex.Message + "\r\n");
                statusTextBox.AppendText(String.Format("Impossibile elaborare il post {0}.\r\n", postCount));
            }
        }

        private void addComment(XmlWriter xw, int postNumber, string link, int commentNumber, XmlNode commentNode, bool dryRun)
        {
            try
            {
                String title = getStringValue(commentNode, "subject");
                String content = getStringValue(commentNode, "body");
                bool isDraft = getIntValue(commentNode, "status") != 1;
                DateTime published = getDateValue(commentNode, "published");
                DateTime updated = getDateValue(commentNode, "updated");
                String author = getAuthor(commentNode);

                if (!dryRun)
                {
                    insertComment(xw, postNumber, commentNumber, title, content, isDraft, published, updated, author, link);
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                statusTextBox.AppendText(ex.Message + "\r\n");
                statusTextBox.AppendText(String.Format("Impossibile elaborare il commento {0} nel post {1}.\r\n", commentCount, postCount));
            }
        }

        private void insertPost(XmlWriter xw, int postNumber, string title, string content, bool isDraft,
            DateTime published, DateTime updated, string author, string link, XmlNode entryNode)
        {
            /*
              <ns0:entry>
                <ns0:category scheme="http://schemas.google.com/g/2005#kind" term="http://schemas.google.com/blogger/2008/kind#post" />
                <ns0:id>post-1</ns0:id>
                <ns0:author>
                  <ns0:name>moridey</ns0:name>
                </ns0:author>
                <ns0:content type="html">Welcome to &lt;a href="http://wordpress.com/"&gt;WordPress.com&lt;/a&gt;. This is your first post. Edit or delete it and start blogging!</ns0:content>
                <ns0:published>2010-08-23T23:51:01Z</ns0:published>
                <ns0:title type="html">Hello world!</ns0:title>
                <ns0:link href="http://moridey.wordpress.com/2010/08/23/hello-world/" rel="self" type="application/atom+xml" />
                <ns0:link href="http://moridey.wordpress.com/2010/08/23/hello-world/" rel="alternate" type="text/html" />
              </ns0:entry>
             */
            xw.WriteStartElement("entry", ATOM_NS);
            buildEntry(xw, "post", String.Format("post-{0}", postNumber), title, content, published, updated, author, link);
            xw.WriteStartElement("link", ATOM_NS);
            xw.WriteAttributeString("rel", "alternate");
            xw.WriteAttributeString("type", "text/html");
            xw.WriteAttributeString("href", link);
            xw.WriteEndElement();

            foreach (XmlNode node in entryNode.ChildNodes)
            {
                if (node.Name == "category")
                {
                    string category = getAttribute(node, "term");
                    if (category != null && category.Length > 0)
                    {
                        insertCategory(xw, category);
                    }
                }
            }

            xw.WriteEndElement();
        }

        private void insertCategory(XmlWriter xw, string term)
        {
            xw.WriteStartElement("category", ATOM_NS);
            xw.WriteAttributeString("scheme", "http://www.blogger.com/atom/ns#");
            xw.WriteAttributeString("term", term);
            xw.WriteEndElement();
        }

        private void insertComment(XmlWriter xw, int postNumber, int commentNumber, string title, string content,
            bool isDraft, DateTime published, DateTime updated, string author, string link)
        {
            /*
              <ns0:entry>
                <ns0:category scheme="http://schemas.google.com/g/2005#kind" term="http://schemas.google.com/blogger/2008/kind#comment" />
                <ns0:id>post-1.comment-1</ns0:id>
                <ns0:author>
                  <ns0:name>Mr WordPress</ns0:name>
                </ns0:author>
                <ns0:content type="html">Hi, this is a comment.&lt;br /&gt;To delete a comment, just log in, and view the posts' comments, there you will have the option to edit or delete them.</ns0:content>
                <ns0:published>2010-08-23T23:51:01Z</ns0:published>
                <ns0:title type="text">Hi, this is a comment.To delete a comment, just l...</ns0:title>
                <ns0:link href="http://moridey.wordpress.com/2010/08/23/hello-world/" rel="self" type="application/atom+xml" />
                <ns1:in-reply-to ref="post-1" type="application/atom+xml" xmlns:ns1="http://purl.org/syndication/thread/1.0" />
              </ns0:entry>
             */
            xw.WriteStartElement("entry", ATOM_NS);
            buildEntry(xw, "comment", String.Format("post-{0}.comment-{1}", postNumber, commentNumber), title,
                content, published, updated, author, link);
            xw.WriteStartElement("ns1", "in-reply-to", PURL_NS);
            xw.WriteAttributeString("ref", String.Format("post-{0}", postNumber));
            xw.WriteAttributeString("type", "application/atom+xml");
            xw.WriteEndElement();
            xw.WriteEndElement();
        }

        private static void buildEntry(XmlWriter xw, string term, string id, string title, string content, DateTime published, DateTime updated,
            string author, string link)
        {
            xw.WriteStartElement("category", ATOM_NS);
            xw.WriteAttributeString("scheme", "http://schemas.google.com/g/2005#kind");
            xw.WriteAttributeString("term", "http://schemas.google.com/blogger/2008/kind#" + term);
            xw.WriteEndElement();
            xw.WriteStartElement("id", ATOM_NS);
            xw.WriteString(id);
            xw.WriteEndElement();
            xw.WriteStartElement("author", ATOM_NS);
            xw.WriteStartElement("name", ATOM_NS);
            xw.WriteString(author);
            xw.WriteEndElement();
            xw.WriteEndElement();
            xw.WriteStartElement("content", ATOM_NS);
            xw.WriteAttributeString("type", "html");
            xw.WriteCData(content);
            xw.WriteEndElement();
            xw.WriteStartElement("published", ATOM_NS);
            xw.WriteString(published.ToString("yyyy-MM-ddTHH\\:mm\\:sszzz"));
            xw.WriteEndElement();
            xw.WriteStartElement("updated", ATOM_NS);
            xw.WriteString(updated.ToString("yyyy-MM-ddTHH\\:mm\\:sszzz"));
            xw.WriteEndElement();
            xw.WriteStartElement("title", ATOM_NS);
            xw.WriteAttributeString("type", "html");
            xw.WriteCData(title);
            xw.WriteEndElement();
            xw.WriteStartElement("link", ATOM_NS);
            xw.WriteAttributeString("rel", "self");
            xw.WriteAttributeString("type", "application/atom+xml");
            xw.WriteAttributeString("href", link);
            xw.WriteEndElement();
        }

        private string getLink(XmlNode node)
        {
            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode.Name == "link")
                    foreach (XmlAttribute attribute in childNode.Attributes)
                    {
                        if (attribute.Name == "href")
                            return attribute.InnerText;
                    }
            }
            return "";
        }

        private string getAttribute(XmlNode node, string name)
        {
            foreach (XmlAttribute attribute in node.Attributes)
            {
                if (attribute.Name == name)
                    return attribute.InnerText;
            }
            return "";
        }

        private string getStringValue(XmlNode node, string name)
        {
            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode.Name == name)
                    return childNode.InnerText;
            }
            return "";
        }

        private String getAuthor(XmlNode node)
        {
            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode.Name == "author")
                    foreach (XmlNode childChildNode in childNode.ChildNodes)
                    {
                        if (childChildNode.Name == "name")
                            return childChildNode.InnerText;
                    }
            }
            return null;
        }

        private DateTime getDateValue(XmlNode node, String name)
        {
            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode.Name == name)
                    return DateTime.Parse(childNode.InnerText);
            }
            return DateTime.Now;
        }

        private int getIntValue(XmlNode node, String name)
        {
            foreach (XmlNode childNode in node.ChildNodes)
            {
                if (childNode.Name == name)
                    return int.Parse(childNode.InnerText);
            }
            return 0;
        }
    }
}
