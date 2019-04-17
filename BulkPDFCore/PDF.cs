using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections;
using iTextSharp.text;
using iTextSharp.text.pdf;
using iTextSharp.text.exceptions;
using System.Text.RegularExpressions;

namespace BulkPDF
{
    public class PDF
    {
        enum AcroFieldsTypes
        {
            BUTTON = 1,
            CHECK_BOX = 2,
            RADIO_BUTTON = 3,
            TEXT_FIELD = 4,
            LIST_BOX = 5,
            COMBO_BOX = 6
        }
        PdfReader pdfReader;
        List<FieldWriteData> writerFieldList = new List<FieldWriteData>();
        public bool IsXFA
        {
            get { return isXFA; }
        }
        bool isXFA = false;
        bool isDynamicXFA = false;

        struct FieldWriteData
        {
            public string Name;
            public string Value;
            public bool MakeReadOnly;
        }

        public void Open(String filePath)
        {
            try
            {
                PdfReader.unethicalreading = true;
                pdfReader = new PdfReader(filePath);
                //pdfReader.RemoveUsageRights();
            }
            catch (InvalidPdfException e)
            {
                throw new Exception(e.ToString());
            }

            // XFA?
            XfaForm xfa = new XfaForm(pdfReader);
            if (xfa != null && xfa.XfaPresent)
            {
                isXFA = true;

                if (xfa.Reader.AcroFields.Fields.Keys.Count == 0)
                {
                    isDynamicXFA = true;
                }
                else
                {
                    isDynamicXFA = false;
                }
            }
            else
            {
                isXFA = false;
            }
        }

        public void Close()
        {
            if (pdfReader != null)
                pdfReader.Close();
        }

        public void SaveFilledPDF(string filePath, bool finalize)
        {
            var copiedPdfReader = new PdfReader(pdfReader);
            var pdfStamperMemoryStream = new MemoryStream();
            PdfStamper pdfStamper = null;
            pdfStamper = new PdfStamper(copiedPdfReader, pdfStamperMemoryStream, '\0', true);

			BaseFont bf = BaseFont.CreateFont(Environment.GetEnvironmentVariable("windir") + @"\fonts\ARIAL.TTF", BaseFont.IDENTITY_H, true);
			Font NormalFont = new iTextSharp.text.Font(bf, 12, Font.NORMAL, BaseColor.BLACK);
			//Font NormalFont = FontFactory.GetFont("Arial", 12, Font.NORMAL, BaseColor.BLACK);
			pdfStamper.AcroFields.AddSubstitutionFont(bf);

			// Fill
			foreach (var field in writerFieldList)
            {
                // Write
                if (isXFA)
                {
                    var node = pdfStamper.AcroFields.Xfa.FindDatasetsNode(field.Name);
                    var text = node.OwnerDocument.CreateTextNode(field.Value);
                    pdfStamper.AcroFields.Xfa.Changed = true;
                }
                else
                {
                    string value = field.Value;

                    // AcroFields radiobutton start by zero -> dataSourceIndex-1 
                    if (pdfStamper.AcroFields.GetFieldType(field.Name) == (int)AcroFieldsTypes.RADIO_BUTTON)
                    {
                        int radiobuttonIndex = 0;
                        if (int.TryParse(field.Value, out radiobuttonIndex))
                        {
                            radiobuttonIndex--;
                            value = radiobuttonIndex.ToString();
                        }
                    }

                    bool test = pdfStamper.AcroFields.SetField(field.Name, value);
                    Console.WriteLine(test); 
                }

                // Read Only
                if (field.MakeReadOnly)
                {
                    if (isDynamicXFA)
                    {
                        // Read only dynamic XFAs
                        if (field.MakeReadOnly && isDynamicXFA)
                        {
                            string name = Regex.Match(field.Name, @"([A-Za-z0-9]+)(\[[0-9]+\]|)$").Groups[1].Value;
                            for (int i = 0; i < pdfStamper.AcroFields.Xfa.DomDocument.SelectNodes("//*[@name='" + name + "']").Count; i++)
                            {
                                var attr = pdfStamper.AcroFields.Xfa.DomDocument.CreateAttribute("access");
                                attr.Value = "readOnly";
                                pdfStamper.AcroFields.Xfa.DomDocument.SelectNodes("//*[@name='" + name + "']")[i].Attributes.Append(attr);
                            }
                        }
                    }
                    else
                    {
                        // Read only for not dynamic XFAs
                        pdfStamper.AcroFields.SetFieldProperty(field.Name, "setfflags", PdfFormField.FF_READ_ONLY, null);
                    }
                }
            }

            // Global Finalize
            if (finalize)
            {
                if (isDynamicXFA)
                {
                    pdfStamper.AcroFields.Xfa.FillXfaForm(pdfStamper.AcroFields.Xfa.DatasetsNode, true);
                }
                else
                {
                    foreach (var field in ListFields())
                        pdfStamper.AcroFields.SetFieldProperty(field.Name, "setfflags", PdfFormField.FF_READ_ONLY, null);
                }
            }


            pdfStamper.Close();
            byte[] content = pdfStamperMemoryStream.ToArray();

            try
            {
                using (var fs = File.Create(filePath))
                {
                    fs.Write(content, 0, (int)content.Length);
                }
            }
            catch (IOException e)
            {
                throw new Exception(e.Message);
            }
        }

        public void SetFieldValue(string fieldname, string value, bool makeReadOnly = false)
        {
            var field = new FieldWriteData();
            field.Name = fieldname;
            field.Value = value;
            field.MakeReadOnly = makeReadOnly;

            writerFieldList.Add(field);
        }

        public void ResetFieldValue()
        {
            writerFieldList.Clear();
        }

        public List<PDFField> ListFields()
        {
            XfaForm xfa = new XfaForm(pdfReader);
            if (isDynamicXFA)
            {
                var acroFields = pdfReader.AcroFields;
                return ListDynamicXFAFields(acroFields.Xfa.DatasetsNode.FirstChild);
            }
            else
            {
                return ListGenericFields();
            }
        }

        private List<PDFField> ListGenericFields()
        {
            var fields = new List<PDFField>();
            var acroFields = pdfReader.AcroFields;

            foreach (var field in acroFields.Fields)
                if (acroFields.GetFieldType(field.Key.ToString()) != (int)AcroFieldsTypes.BUTTON)
                {
                    // If readonly, continue loop
                    var n = field.Value.GetMerged(0).GetAsNumber(PdfName.FF);
                    if (n != null && ((n.IntValue & (int)PdfFormField.FF_READ_ONLY) > 0))
                    {
                        continue;
                    }
                    else
                    {
                        var pdfField = new PDFField();
                        pdfField.Name = field.Key.ToString();
                        pdfField.CurrentValue = acroFields.GetField(field.Key.ToString());
                        try
                        {
                            pdfField.Typ = Convert.ToString((AcroFieldsTypes)acroFields.GetFieldType(pdfField.Name));
                        }
                        catch (Exception)
                        {
                            pdfField.Typ = "";
                        }

                        fields.Add(pdfField);
                    }
                }

            return fields;
        }

        private List<PDFField> ListDynamicXFAFields(System.Xml.XmlNode n)
        {
            List<PDFField> pdfFields = new List<PDFField>();

            foreach (System.Xml.XmlNode child in n.ChildNodes) // > 0 Childs == Group
                pdfFields.AddRange(ListDynamicXFAFields(child)); // Search field

            if (n.ChildNodes.Count == 0) // 0 Childs == Field 
            {
                var acroFields = pdfReader.AcroFields;

                var pdfField = new PDFField();

                // If a value is set the value of n.Name would be "#text"
                if ((n.Name.ToCharArray(0, 1))[0] != '#')
                {
                    pdfField.Name = acroFields.GetTranslatedFieldName(n.Name);
                }
                else
                {
                    pdfField.Name = acroFields.GetTranslatedFieldName(n.ParentNode.Name);
                }

                pdfField.CurrentValue = n.Value;

                pdfField.Typ = "";

                pdfFields.Add(pdfField);

                return pdfFields;
            }

            return pdfFields;
        }
    }
}
