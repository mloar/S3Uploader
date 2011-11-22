using System;
using System.Configuration;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.SimpleDB;
using Amazon.SimpleDB.Model;
using Amazon.S3;
using Amazon.S3.Model;

namespace AWS_Console_App1
{
    public class Program
    {
        static void Blah(object o, UploadPartProgressArgs args)
        {
            Console.CursorLeft = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("[");
            int num = args.PercentDone;
            while (num-- > 0)
            {
                sb.Append("=");
            }
            num = 100 - args.PercentDone;
            while (num-- > 0)
            {
                sb.Append(" ");
            }
            sb.Append("]");
            if (args.PercentDone == 100)
            {
                sb.Append("\n");
            }

            Console.Write(sb);
        }

        public class UploadState
        {
            public string FileName;
            public string BucketName;
            public string Key;
            public string UploadId;
            public long ChunkSize;
            public int NumChunks;
            public long FileLength;
            public long FilePosition;
            public int PartNumber;
            public System.Collections.Generic.List<PartETag> Responses;

            public UploadState()
            {
            }

            public UploadState(string fileName, string bucketName)
            {
                FileName = fileName;
                BucketName = bucketName;
                FileInfo fi = new FileInfo(fileName);

                if (!fi.Exists)
                {
                    throw new ArgumentException("File does not exist", fileName);
                }
                FileLength = fi.Length;

                // Amazon requires that each chunk be at least 5 MB
                if (FileLength < 5 * (1 << 20))
                {
                    ChunkSize = FileLength;
                    NumChunks = 1;
                }
                else
                {
                    // Integer division results in truncation of result, so we end up
                    // with the smallest ChunkSize > 5 MB.
                    NumChunks = (int)(FileLength / (5 * (1 << 20)));
                    ChunkSize = (long)Math.Floor((double)FileLength / NumChunks);
                }

                FilePosition = 0;
                PartNumber = 1;

                Responses = new System.Collections.Generic.List<PartETag>(NumChunks);

                Key = Path.GetFileName(fileName);
            }
        }

        public static void Main(string[] args)
        {
            XmlSerializerFactory factory = new XmlSerializerFactory();
            XmlSerializer serializer = factory.CreateSerializer(typeof(UploadState));

            UploadState us;
            if (args.Length == 2)
            {
                us = new UploadState(args[1], args[0]);
            }
            else if (args.Length == 0 && new FileInfo("upload.dat").Exists)
            {
                using (FileStream fs = new FileStream("upload.dat", FileMode.Open))
                {
                    us = (UploadState)serializer.Deserialize(fs);
                }
            }
            else
            {
                Console.Error.WriteLine("Usage: bucket filename");
                return;
            }

            try
            {
                NameValueCollection appConfig = ConfigurationManager.AppSettings;
                // Print the number of Amazon S3 Buckets.
                AmazonS3 s3Client = AWSClientFactory.CreateAmazonS3Client(
                    appConfig["AWSAccessKey"],
                    appConfig["AWSSecretKey"]
                    );

                if (string.IsNullOrEmpty(us.UploadId))
                {

                    InitiateMultipartUploadRequest req =
                        new InitiateMultipartUploadRequest()
                        .WithBucketName(us.BucketName)
                        .WithKey(us.Key)
                        ;

                    us.UploadId = s3Client.InitiateMultipartUpload(req).UploadId;

                    using (FileStream fs = new FileStream("upload.dat", FileMode.OpenOrCreate))
                    {
                        serializer.Serialize(fs, us);
                    }
                }

                while (us.FilePosition < us.FileLength)
                {
                    try
                    {
                        Console.WriteLine("Uploading part {0} of {1}", us.PartNumber, us.NumChunks);
                        UploadPartRequest ureq = new UploadPartRequest()
                        .WithBucketName(us.BucketName)
                        .WithFilePath(us.FileName)
                        .WithFilePosition(us.FilePosition)
                        .WithPartNumber(us.PartNumber)
                        .WithPartSize(us.FileLength - us.FilePosition > us.ChunkSize ? us.ChunkSize : us.FileLength - us.FilePosition)
                        .WithGenerateChecksum(true)
                        .WithKey(us.Key)
                        .WithUploadId(us.UploadId)
                        .WithSubscriber(new EventHandler<UploadPartProgressArgs>(Blah))
                        ;

                        if (us.Responses.Count > us.PartNumber - 1)
                        {
                            us.Responses[us.PartNumber - 1] = new PartETag(us.PartNumber, s3Client.UploadPart(ureq).ETag);
                        }
                        else
                        {
                            us.Responses.Insert(us.PartNumber - 1, new PartETag(us.PartNumber, s3Client.UploadPart(ureq).ETag));
                        }
                        us.PartNumber++;
                        us.FilePosition += us.ChunkSize;

                        using (FileStream fs = new FileStream("upload.dat", FileMode.OpenOrCreate))
                        {
                            serializer.Serialize(fs, us);
                        }
                    }
                    catch (System.Net.WebException)
                    {
                    }
                }

                CompleteMultipartUploadRequest creq = new CompleteMultipartUploadRequest()
                .WithPartETags(us.Responses)
                .WithBucketName(us.BucketName)
                .WithUploadId(us.UploadId)
                .WithKey(us.Key)
                ;

                CompleteMultipartUploadResponse cresp = s3Client.CompleteMultipartUpload(creq);
                System.Console.WriteLine("File available at {0}", cresp.Location);

                File.Delete("upload.dat");
            }
            catch (AmazonS3Exception e)
            {
                Console.Error.WriteLine(e);
            }

            //Console.Write(GetServiceOutput());
            //Console.Read();
        }

        public static string GetServiceOutput()
        {
            StringBuilder sb = new StringBuilder(1024);
            using (StringWriter sr = new StringWriter(sb))
            {
                NameValueCollection appConfig = ConfigurationManager.AppSettings;

                sr.WriteLine("===========================================");
                sr.WriteLine("Welcome to the AWS .NET SDK!");
                sr.WriteLine("===========================================");

                // Print the number of Amazon EC2 instances.
                AmazonEC2 ec2 = AWSClientFactory.CreateAmazonEC2Client(
                    appConfig["AWSAccessKey"],
                    appConfig["AWSSecretKey"]
                    );
                DescribeInstancesRequest ec2Request = new DescribeInstancesRequest();

                try
                {
                    DescribeInstancesResponse ec2Response = ec2.DescribeInstances(ec2Request);
                    int numInstances = 0;
                    numInstances = ec2Response.DescribeInstancesResult.Reservation.Count;
                    sr.WriteLine("You have " + numInstances + " Amazon EC2 instance(s) running in the US-East (Northern Virginia) region.");

                }
                catch (AmazonEC2Exception ex)
                {
                    if (ex.ErrorCode != null && ex.ErrorCode.Equals("AuthFailure"))
                    {
                        sr.WriteLine("The account you are using is not signed up for Amazon EC2.");
                        sr.WriteLine("You can sign up for Amazon EC2 at http://aws.amazon.com/ec2");
                    }
                    else
                    {
                        sr.WriteLine("Caught Exception: " + ex.Message);
                        sr.WriteLine("Response Status Code: " + ex.StatusCode);
                        sr.WriteLine("Error Code: " + ex.ErrorCode);
                        sr.WriteLine("Error Type: " + ex.ErrorType);
                        sr.WriteLine("Request ID: " + ex.RequestId);
                        sr.WriteLine("XML: " + ex.XML);
                    }
                }
                sr.WriteLine();

                // Print the number of Amazon SimpleDB domains.
                AmazonSimpleDB sdb = AWSClientFactory.CreateAmazonSimpleDBClient(
                    appConfig["AWSAccessKey"],
                    appConfig["AWSSecretKey"]
                    );
                ListDomainsRequest sdbRequest = new ListDomainsRequest();

                try
                {
                    ListDomainsResponse sdbResponse = sdb.ListDomains(sdbRequest);

                    if (sdbResponse.IsSetListDomainsResult())
                    {
                        int numDomains = 0;
                        numDomains = sdbResponse.ListDomainsResult.DomainName.Count;
                        sr.WriteLine("You have " + numDomains + " Amazon SimpleDB domain(s) in the US-East (Northern Virginia) region.");
                    }
                }
                catch (AmazonSimpleDBException ex)
                {
                    if (ex.ErrorCode != null && ex.ErrorCode.Equals("AuthFailure"))
                    {
                        sr.WriteLine("The account you are using is not signed up for Amazon SimpleDB.");
                        sr.WriteLine("You can sign up for Amazon SimpleDB at http://aws.amazon.com/simpledb");
                    }
                    else
                    {
                        sr.WriteLine("Caught Exception: " + ex.Message);
                        sr.WriteLine("Response Status Code: " + ex.StatusCode);
                        sr.WriteLine("Error Code: " + ex.ErrorCode);
                        sr.WriteLine("Error Type: " + ex.ErrorType);
                        sr.WriteLine("Request ID: " + ex.RequestId);
                        sr.WriteLine("XML: " + ex.XML);
                    }
                }
                sr.WriteLine();

                // Print the number of Amazon S3 Buckets.
                AmazonS3 s3Client = AWSClientFactory.CreateAmazonS3Client(
                    appConfig["AWSAccessKey"],
                    appConfig["AWSSecretKey"]
                    );

                try
                {
                    ListBucketsResponse response = s3Client.ListBuckets();
                    int numBuckets = 0;
                    if (response.Buckets != null &&
                        response.Buckets.Count > 0)
                    {
                        numBuckets = response.Buckets.Count;
                    }
                    sr.WriteLine("You have " + numBuckets + " Amazon S3 bucket(s) in the US Standard region.");
                }
                catch (AmazonS3Exception ex)
                {
                    if (ex.ErrorCode != null && (ex.ErrorCode.Equals("InvalidAccessKeyId") ||
                        ex.ErrorCode.Equals("InvalidSecurity")))
                    {
                        sr.WriteLine("Please check the provided AWS Credentials.");
                        sr.WriteLine("If you haven't signed up for Amazon S3, please visit http://aws.amazon.com/s3");
                    }
                    else
                    {
                        sr.WriteLine("Caught Exception: " + ex.Message);
                        sr.WriteLine("Response Status Code: " + ex.StatusCode);
                        sr.WriteLine("Error Code: " + ex.ErrorCode);
                        sr.WriteLine("Request ID: " + ex.RequestId);
                        sr.WriteLine("XML: " + ex.XML);
                    }
                }
                sr.WriteLine("Press any key to continue...");
            }
            return sb.ToString();
        }
    }
}
