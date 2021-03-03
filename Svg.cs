using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Xml.XPath;
using System.Diagnostics;

namespace SimplifySvg
{
    class Svg
    {
        const double c_padding = 10;
        const string c_nsSvg = "http://www.w3.org/2000/svg";

        XmlDocument m_doc;

        public Svg(string filename)
        {
            m_doc = new XmlDocument();
            m_doc.Load(filename);
        }

        public void WriteTo(string filename)
        {
            var settings = new XmlWriterSettings();
            settings.Indent = true;
            using (var writer = XmlWriter.Create(filename, settings))
            {
                m_doc.Save(writer);
            }
        }

        public void Simplify()
        {
            RemoveClipping();
            ConvertTextTransformations();
            PromoteTspan();

            double width, height, xMin, yMin, xMax, yMax;
            GetDimensions(out width, out height, out xMin, out yMin, out xMax, out yMax);
            Console.WriteLine($"width={width} height={height} xMin={xMin} yMin={yMin} xMax={xMax} yMax={yMax}");

            // Determine new viewport
            double vx = xMin - c_padding;
            if (vx < 0.0) vx = 0.0;
            double vy = yMin - c_padding;
            if (vy < 0.0) vy = 0.0;
            double vWidth = (xMax - xMin) + c_padding * 2;
            double vHeight = (yMax - yMin) + c_padding * 2;

            Translate(-vx, -vy, vWidth, vHeight);
        }

        private void RemoveClipping()
        {
            var node = m_doc.CreateNavigator();

            for (; ; )
            {
                XPathNavigator next;

                if (node.Name == "defs")
                {
                    next = node.Clone();
                    if (!next.MoveToNext(XPathNodeType.Element)) next = null;
                    node.DeleteSelf();
                }

                else if (node.Name == "g")
                {
                    var child = node.Clone();
                    if (child.MoveToFirstChild())
                    {
                        do
                        {
                            node.InsertBefore(child);
                        } while (child.MoveToNext(XPathNodeType.Element));
                    }

                    next = node.Clone();
                    if (!next.MoveToNext(XPathNodeType.Element)) next = null;
                    node.DeleteSelf();
                }

                else
                {
                    next = node;
                    if (!next.MoveToFollowing(XPathNodeType.Element)) next = null;
                }

                if (next == null) break;
                node = next;
            }
        }

        private void ConvertTextTransformations()
        {
            var node = m_doc.CreateNavigator();
            while (node.MoveToFollowing(XPathNodeType.Element))
            {
                if (node.Name == "text")
                {
                    var transform = node.GetAttribute("transform", string.Empty);
                    if (transform.StartsWith("translate("))
                    {
                        var args = transform.Substring(10, transform.IndexOf(')', 10)-10)
                            .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var x = double.Parse(args[0]);
                        var y = double.Parse(args[1]);
                        x += GetAttrDbl(node, "x");
                        y += GetAttrDbl(node, "y");
                        node.CreateAttribute(null, "x", null, x.ToString());
                        node.CreateAttribute(null, "y", null, y.ToString());
                        RemoveAttr(node, "transform");
                    }
                }
            }
        }

        private void PromoteTspan()
        {
            var node = m_doc.CreateNavigator();
            if (node.MoveToFollowing(XPathNodeType.Element))
            {
                for (; ; )
                {
                    if (node.Name == "tspan")
                    {
                        var parent = node.Clone();
                        parent.MoveToParent();
                        var parentX = GetAttrDbl(parent, "x");
                        var parentY = GetAttrDbl(parent, "y");
                        parent.InsertElementAfter(null, "text", c_nsSvg, node.Value);

                        var newNode = parent.Clone();
                        newNode.MoveToNext(XPathNodeType.Element);
                        CopyAttributes(parent, newNode);
                        CopyAttributes(node, newNode);
                        SetAttr(newNode, "x", parentX + GetAttrDbl(node, "x"));
                        SetAttr(newNode, "y", parentY + GetAttrDbl(node, "y"));

                        node.DeleteSelf();
                        node = newNode;
                        if (!node.MoveToNext(XPathNodeType.Element)) break;
                    }
                    else
                    {
                        if (!node.MoveToFollowing(XPathNodeType.Element)) break;
                    }
                }
            }
        }

        void CopyAttributes(XPathNavigator src, XPathNavigator dst)
        {
            var node = src.Clone();
            if (node.MoveToFirstAttribute())
            {
                do
                {
                    SetAttr(dst, node.Name, node.Value);
                }
                while (node.MoveToNextAttribute());
            }
        }

        void Translate(double dx, double dy, double maxWidth, double maxHeight)
        {
            var node = m_doc.CreateNavigator();
            node.MoveToChild(XPathNodeType.Element);

            Debug.Assert(node.Name == "svg");
            TransformNode(node, dx, dy, maxWidth, maxHeight);

            // Handle all children (but not recursively)
            if (node.MoveToChild(XPathNodeType.Element))
            {
                do
                {
                    TransformNode(node, dx, dy, maxWidth, maxHeight);
                }
                while (node.MoveToNext(XPathNodeType.Element));
            }
        }

        void TransformNode(XPathNavigator node, double dx, double dy, double maxWidth, double maxHeight)
        {
            if (node.Name == "path")
            {
                TransformPathNode(node, dx, dy);
                return;
            }

            double x = 0.0;
            double y = 0.0;
            if (HasAttr(node, "x") && HasAttr(node, "y"))
            {
                x = GetAttrDbl(node, "x") + dx;
                if (x < 0.0) x = 0;
                y = GetAttrDbl(node, "y") + dy;
                if (y < 0.0) y = 0;
                SetAttr(node, "x", x);
                SetAttr(node, "y", y);
            }

            if (HasAttr(node, "width") && HasAttr(node, "height"))
            {
                double width = GetAttrDbl(node, "width");
                if (width > maxWidth-x)
                {
                    width = maxWidth - x;
                    SetAttr(node, "width", width);
                }

                double height = GetAttrDbl(node, "height");
                if (height > maxHeight - y)
                {
                    height = maxHeight - y;
                    SetAttr(node, "height", height);
                }
            }
        }

        void TransformPathNode(XPathNavigator node, double dx, double dy)
        {
            var reader = new PathReader(node.GetAttribute("d", string.Empty));
            bool lastCommandRelative = false;
            bool lastWasCommand = true;
            var sb = new StringBuilder();
            while (reader.ReadNext())
            {
                if (reader.Command != '\0')
                {
                    lastCommandRelative = char.IsLower(reader.Command);
                    sb.Append(reader.Command);
                    lastWasCommand = true;
                }
                else
                {
                    if (!lastWasCommand)
                        sb.Append(' ');

                    if (lastCommandRelative)
                    {
                        sb.Append(reader.X);
                        sb.Append(' ');
                        sb.Append(reader.Y);
                    }
                    else
                    {
                        sb.Append(reader.X + dx);
                        sb.Append(' ');
                        sb.Append(reader.Y + dy);
                    }
                    lastWasCommand = false;
                }
            }
            SetAttr(node, "d", sb.ToString());
        }

        void GetDimensions(out double width, out double height, out double xMin, out double yMin, out double xMax, out double yMax)
        {
            width = height = 0.0;
            xMin = yMin = double.MaxValue;
            xMax = yMax = 0.0;
            GetDimensionsRecursive(m_doc.CreateNavigator(), 0, 0, ref width, ref height, ref xMin, ref yMin, ref xMax, ref yMax);
        }

        void GetDimensionsRecursive(XPathNavigator parent, double x, double y, ref double width, ref double height, ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            var node = parent.Clone();
            if (!node.MoveToChild(XPathNodeType.Element)) return;

            do
            {
                double nx = x;
                double ny = y;

                Console.WriteLine(node.Name);
                if (node.Name == "svg")
                {
                    width = GetAttrDbl(node, "width");
                    height = GetAttrDbl(node, "height");
                }

                else if (node.Name == "path")
                {
                    GetDimensionsPath(node, x, y, ref xMin, ref yMin, ref xMax, ref yMax);
                }

                else
                {
                    nx = GetAttrDbl(node, "x") + x;
                    ny = GetAttrDbl(node, "y") + y;
                    var dx = GetAttrDbl(node, "width");
                    var dy = GetAttrDbl(node, "height");

                    if ((nx == 0.0 && ny == 0.0)
                        && ((dx == 0.0 && dy == 0.0)
                            || dx == width && dy == height))
                        continue;

                    if (xMin > nx) xMin = nx;
                    if (yMin > ny) yMin = ny;
                    if (xMax < nx + dx) xMax = nx + dx;
                    if (yMax < ny + dy) yMax = ny + dy;
                }

                if (node.HasChildren)
                {
                    GetDimensionsRecursive(node, nx, ny, ref width, ref height, ref xMin, ref yMin, ref xMax, ref yMax);
                }
            }
            while (node.MoveToNext(XPathNodeType.Element));
        }

        static void GetDimensionsPath(XPathNavigator node, double x, double y, ref double xMin, ref double yMin, ref double xMax, ref double yMax)
        {
            var reader = new PathReader(node.GetAttribute("d", string.Empty));
            bool lastCommandRelative = false;
            while (reader.ReadNext())
            {
                if (reader.Command != '\0')
                {
                    lastCommandRelative = char.IsLower(reader.Command);
                }
                else
                {
                    if (lastCommandRelative)
                    {
                        x += reader.X;
                        y += reader.Y;
                    }
                    else
                    {
                        x = reader.X;
                        y = reader.Y;
                    }

                    if (xMin > x) xMin = x;
                    if (yMin > y) yMin = y;
                    if (xMax < x) xMax = x;
                    if (yMax < y) yMax = y;
                }
            }
        }

        static bool HasAttr(XPathNavigator node, string attr)
        {
            return !string.IsNullOrEmpty(node.GetAttribute(attr, string.Empty));
        }

        static double GetAttrDbl(XPathNavigator node, string attr)
        {
            var value = node.GetAttribute(attr, string.Empty);
            double dvalue;
            if (double.TryParse(value, out dvalue)) return dvalue;
            return 0;
        }

        static void RemoveAttr(XPathNavigator node, string attr)
        {
            node = node.Clone();
            if (!node.MoveToAttribute(attr, string.Empty)) return;
            node.DeleteSelf();
        }

        static void SetAttr(XPathNavigator node, string attr, string value)
        {
            node = node.Clone();
            if (node.MoveToAttribute(attr, string.Empty))
            {
                node.SetValue(value);
            }
            else
            {
                node.CreateAttribute(null, attr, null, value);
            }
        }

        static void SetAttr(XPathNavigator node, string attr, double value)
        {
            node = node.Clone();
            if (node.MoveToAttribute(attr, string.Empty))
            {
                node.SetValue(value.ToString());
            }
            else
            {
                node.CreateAttribute(null, attr, null, value.ToString());
            }
        }

        class PathReader
        {
            string m_path;
            int m_index;
            int m_end;

            public char Command { get; private set; } // '\0' for Dimensions
            public double X { get; private set; }
            public double Y { get; private set; }

            public PathReader(string path)
            {
                m_path = path;
                m_index = 0;
                m_end = m_path.Length;
            }

            public bool ReadNext()
            {
                while (m_index < m_end && char.IsWhiteSpace(m_path[m_index])) ++m_index;
                if (m_index >= m_end) return false;

                if (m_index < m_end && (char.IsDigit(m_path[m_index]) || (m_path[m_index] == '.')))
                {
                    X = GetNextDouble();
                    Y = GetNextDouble();
                    Command = '\0';
                    return true;
                }
                else
                {
                    Command = m_path[m_index];
                    ++m_index;
                    X = 0.0;
                    Y = 0.0;
                    return true;
                }
            }

            double GetNextDouble()
            {
                while (m_index < m_end && char.IsWhiteSpace(m_path[m_index])) ++m_index;
                int a = m_index;
                ++m_index;
                while (m_index < m_end && (char.IsDigit(m_path[m_index]) || (m_path[m_index] == '.')))
                    ++m_index;

                return double.Parse(m_path.Substring(a, m_index - a));
            }
        }
    }
}
