﻿/*
 *  This file is part of Xades Lib.
 *  Copyright (C) 2012 I.M. vzw
 *
 *  Xades Lib is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU Lesser General Public License as published by
 *  the Free Software Foundation, either version 2.1 of the License, or
 *  (at your option) any later version.
 *
 *  Foobar is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with Xades Lib.  If not, see <http://www.gnu.org/licenses/>.
 */

using IM.Xades;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Text;
using System.Security.Cryptography.Xml;
using System.IO;
using System.Xml.Serialization;
using Siemens.EHealth.Client.Sso.Sts;
using System.ServiceModel;
using System.ServiceModel.Description;
using Siemens.EHealth.Client.Sso.WA;
using IM.Xades.Extra;

namespace IM.Xades.Test
{
    
    
    [TestClass]
    public class XadesTest
    {
        private static XmlDocument document;
        private static IntModule.Blob detail;

        private static X509Certificate2 auth;
        private static X509Certificate2 sign;
        private static TSA.DSS.TimeStampAuthorityClient tsa;
        private static IntModule.XadesToolsClient im;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            //load certificates (fixed)
            auth = new X509Certificate2("MYCARENET.p12", "mycarenet");

            //load certificate (eid)
            X509Store store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            X509Certificate2Collection canidateCerts = store.Certificates.Find(X509FindType.FindByKeyUsage, X509KeyUsageFlags.NonRepudiation, true);
            X509Certificate2Collection selectedCerts = X509Certificate2UI.SelectFromCollection(canidateCerts, "Select cert", "Select your signing cert", X509SelectionFlag.SingleSelection);
            sign = selectedCerts[0];
            //sign = auth;


            //load test document as xml
            document = new XmlDocument();
            document.PreserveWhitespace = true;
            document.Load(@"document.xml");

            //load the document as POCO
            XmlSerializer serializer = new XmlSerializer(typeof(IntModule.Blob));
            StringWriter writer = new StringWriter();
            serializer.Serialize(writer, new IntModule.Blob());
            System.Console.WriteLine(writer.ToString());
            detail = (IntModule.Blob)serializer.Deserialize(new FileStream(@"document.xml", FileMode.Open));

            //create the tsa
            tsa = new TSA.DSS.TimeStampAuthorityClient(new StsBinding(), new EndpointAddress("https://wwwacc.ehealth.fgov.be/timestampauthority_1_5/timestampauthority"));
            tsa.Endpoint.Behaviors.Remove<ClientCredentials>();
            tsa.Endpoint.Behaviors.Add(new OptClientCredentials());
            tsa.ClientCredentials.ClientCertificate.Certificate = auth;

            var binding = new WSHttpBinding(SecurityMode.TransportWithMessageCredential, false);
            binding.MessageEncoding = WSMessageEncoding.Mtom;
            binding.Security.Message.EstablishSecurityContext = false;
            binding.Security.Message.ClientCredentialType = MessageCredentialType.Certificate;
            im = new IntModule.XadesToolsClient(binding, new EndpointAddress("https://dev.mycarenet.be/im-ws/XadesTools"));
            im.ClientCredentials.ClientCertificate.Certificate = auth;
        }

        private TestContext testContextInstance;

        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        // 
        //You can use the following additional attributes as you write your tests:
        //
        //Use ClassInitialize to run code before running the first test in the class
        //[ClassInitialize()]
        //public static void MyClassInitialize(TestContext testContext)
        //{
        //}
        //
        //Use ClassCleanup to run code after all tests in a class have run
        //[ClassCleanup()]
        //public static void MyClassCleanup()
        //{
        //}
        //
        //Use TestInitialize to run code before running each test
        //[TestInitialize()]
        //public void MyTestInitialize()
        //{
        //}
        //
        //Use TestCleanup to run code after each test has run
        //[TestCleanup()]
        //public void MyTestCleanup()
        //{
        //}
        //
        #endregion

        [TestMethod]
        public void VerifyXadesTTest()
        {
            byte[] xades = im.createXadesT(detail);

            XmlDocument xadesDoc = new XmlDocument();
            xadesDoc.PreserveWhitespace = true;
            xadesDoc.Load(new MemoryStream(xades));

            var xml = new StringBuilder();
            var writerSettings = new XmlWriterSettings
            {
                Indent = true
            };
            using (var writer = XmlWriter.Create(xml, writerSettings))
            {
                xadesDoc.WriteTo(writer);
            }
            System.Console.WriteLine(xml.ToString());

            var xerifier = new XadesVerifier();

            var info = xerifier.Verify(document, (XmlElement) XadesTools.FindXadesProperties(xadesDoc)[0]);

            Assert.IsNotNull(info);
            Assert.AreEqual("CN=\"CBE=0820563481, MYCARENET\", OU=eHealth-platform Belgium, OU=CIN-NIC, OU=\"CBE=0820563481\", OU=MYCARENET, O=Federal Government, C=BE", info.Certificate.Subject);
            Assert.AreEqual(XadesForm.XadesT, info.Form);
            Assert.AreEqual(0.0, info.Time.Value.Offset.TotalHours);
        }

        [TestMethod]
        public void RoundTestXadesBes()
        {
            var xigner = new XadesCreator(sign);
            xigner.TimestampProvider = new TSA.EHealthTimestampProvider(tsa);
            xigner.DataTransforms.Add(new XmlDsigBase64Transform());
            xigner.DataTransforms.Add(new OptionalDeflateTransform());
            var xades = xigner.CreateXadesBes(document, "_D4840C96-8212-491C-9CD9-B7144C1AD450");

            //Output for debugging
            var xml = new StringBuilder();
            var writerSettings = new XmlWriterSettings
            {
                Indent = true
            };
            using (var writer = XmlWriter.Create(xml, writerSettings))
            {
                xades.WriteTo(writer);
            }
            System.Console.WriteLine(xml.ToString());

            //Output for reading
            MemoryStream stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream))
            {
                xades.WriteTo(writer);
            }
            stream.Seek(0, SeekOrigin.Begin);

            var xades2 = new XmlDocument();
            xades2.PreserveWhitespace = true;
            xades2.Load(stream);

            var xerifier = new XadesVerifier();
            var info = xerifier.Verify(document, (XmlElement)XadesTools.FindXadesProperties(xades2)[0]);

            Assert.IsNotNull(info);
            Assert.IsNotNull(info.Certificate);
            Assert.AreEqual(XadesForm.XadesBes, info.Form);
            Assert.AreEqual(0.0, info.Time.Value.Offset.TotalHours);
        }

        [TestMethod]
        public void RoundTestXadesT()
        {
            var xigner = new XadesCreator(sign);
            xigner.TimestampProvider = new TSA.EHealthTimestampProvider(tsa);
            xigner.DataTransforms.Add(new XmlDsigBase64Transform());
            xigner.DataTransforms.Add(new OptionalDeflateTransform());
            var xades = xigner.CreateXadesT(document, "_D4840C96-8212-491C-9CD9-B7144C1AD450");

            MemoryStream stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream))
            {
                xades.WriteTo(writer);
            }
            stream.Seek(0, SeekOrigin.Begin);

            var xades2 = new XmlDocument();
            xades2.PreserveWhitespace = true;
            xades2.Load(stream);

            var xerifier = new XadesVerifier();
            var info = xerifier.Verify(document, (XmlElement)XadesTools.FindXadesProperties(xades2)[0]);

            Assert.IsNotNull(info);
        }

        [TestMethod]
        public void RoundTestXadesTViaFedict()
        {
            var xigner = new XadesCreator(sign);
            xigner.TimestampProvider = new TSA.Rfc3161TimestampProvider();
            xigner.DataTransforms.Add(new XmlDsigBase64Transform());
            xigner.DataTransforms.Add(new OptionalDeflateTransform());
            var xades = xigner.CreateXadesT(document, "_D4840C96-8212-491C-9CD9-B7144C1AD450");

            //Output for debugging
            var xml = new StringBuilder();
            var writerSettings = new XmlWriterSettings
            {
                Indent = true
            };
            using (var writer = XmlWriter.Create(xml, writerSettings))
            {
                xades.WriteTo(writer);
            }
            System.Console.WriteLine(xml.ToString());

            //Output for reading
            MemoryStream stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream))
            {
                xades.WriteTo(writer);
            }
            stream.Seek(0, SeekOrigin.Begin);

            var xades2 = new XmlDocument();
            xades2.PreserveWhitespace = true;
            xades2.Load(stream);

            var xerifier = new XadesVerifier();
            var info = xerifier.Verify(document, (XmlElement)XadesTools.FindXadesProperties(xades2)[0]);

            Assert.IsNotNull(info);
            Assert.IsNotNull(info.Certificate);
            Assert.AreEqual(XadesForm.XadesT, info.Form);
            Assert.AreEqual(0.0, info.Time.Value.Offset.TotalHours);
        }

        [TestMethod]
        public void CreatXadesTTest()
        {

            var xigner = new XadesCreator(sign);
            xigner.TimestampProvider = new TSA.EHealthTimestampProvider(tsa);
            xigner.DataTransforms.Add(new XmlDsigBase64Transform());
            xigner.DataTransforms.Add(new OptionalDeflateTransform());
            var xades = xigner.CreateXadesT(document, "_D4840C96-8212-491C-9CD9-B7144C1AD450");

            Assert.IsNotNull(xades);

            var xml = new StringBuilder();
            var writerSettings = new XmlWriterSettings
            {
                Indent = true
            };
            using (var writer = XmlWriter.Create(xml, writerSettings))
            {
                xades.WriteTo(writer);
            }
            System.Console.WriteLine(xml.ToString());


            MemoryStream stream = new MemoryStream();
            using (var writer = XmlWriter.Create(stream))
            {
                xades.WriteTo(writer);
            }

            IntModule.VerificationResult vResult = im.verifyXadesT(detail, stream.GetBuffer());
            
            System.Console.WriteLine(vResult.ResultMajor);
            System.Console.WriteLine(vResult.ResultMinor);
            System.Console.WriteLine(vResult.ResultMessage);

            Assert.AreEqual("urn:nip:tack:result:major:success", vResult.ResultMajor);
        }
    }
}