﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.ServiceModel.Syndication;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Security.Cryptography;
using System.Text;
using LiteDB;

namespace StalkerProject.OutputTerminal
{
    public class RssTerminal : ISTKService
    {
        public int Interval { get; set; }
        public string Alias { get; set; }
        public int FeedId { get; set; }
        public string FeedName { get; set; }
        private LiteDatabase database=null;
        private SyndicationFeed feed;
        private Task updateJob;
        private CancellationTokenSource isCancel;
        public void Start()
        {
            
            feed=new SyndicationFeed(FeedName,"Provided By StalkerProject",
                new Uri("http://127.0.0.1"),"id=" + FeedId.ToString(),DateTime.Now);
            isCancel=new CancellationTokenSource();
            updateJob=new Task(() => { UpdateLoop(isCancel.Token);},isCancel.Token);
            updateJob.Start();
        }

        public void GetDatabase(LiteDatabase db)
        {
            database = db;
        }

        static SHA256 hash = new SHA256Managed();
        public static string GetStringHash(string str)
        {
            {
                byte[] data = hash.ComputeHash(Encoding.Default.GetBytes(str));
                StringBuilder sBuilder = new StringBuilder();

                // Loop through each byte of the hashed data 
                // and format each one as a hexadecimal string.
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }
                return sBuilder.ToString();
            }
        }

        public void UpdateLoop(CancellationToken token)
        {
            token.WaitHandle.WaitOne(10000);//WaitFor 10 seconds
            if (database == null)
            {
                Console.WriteLine("No DiffDatabase connected,Service Terminate");
            }
            var col = database.GetCollection<OutputData>();
            DateTime updateTime=DateTime.Now;
            for (;;)
            {
                
                //Rebuild RssData
                try
                {
                    var iter = col.Find(Query.All(Query.Descending), limit: 60);
                    List<SyndicationItem> item = new List<SyndicationItem>();
                    bool isFirst = true;
                    foreach (var val in iter)
                    {
                        if (isFirst)
                        {
                            isFirst = false;
                            updateTime = val.OutputTime;
                        }
                        SyndicationItem sitem = new SyndicationItem()
                        {
                            Title = new TextSyndicationContent(val.Summary),
                            //Summary = SyndicationContent.CreatePlaintextContent(val.Summary),
                            Content = SyndicationContent.CreatePlaintextContent(val.Content),
                            PublishDate = val.OutputTime,
                            LastUpdatedTime = val.OutputTime,
                            Links = { new SyndicationLink(new Uri(val.RelatedAddress)) },
                            Id=GetStringHash(val.Summary)
                        };
                        item.Add(sitem);
                    }
                    feed.Items = item;
                    feed.LastUpdatedTime = updateTime;
                    Console.WriteLine("RssData Updated");

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    string outputstr=e.ToString() + "\n";
                    string anotherPart = "模块RssTerminal发生了异常!\n"
                                         + e.StackTrace
                                         + "\n"
                                         + e.InnerException;
                    File.AppendAllText("ErrorDump.txt",outputstr+anotherPart);
                }
                token.WaitHandle.WaitOne(Math.Max(60000, Interval));
                token.ThrowIfCancellationRequested();
            }
        }

        public void Stop()
        {
            if (updateJob.IsCompleted)
            {
                isCancel.Dispose();
                return;
            }
            isCancel.Cancel();
            try
            {
                updateJob.Wait();
            }
            catch (AggregateException e)
            {
                foreach (var v in e.InnerExceptions)
                    Console.WriteLine(e.Message + " " + v.Message);
            }
            finally
            {
                isCancel.Dispose();
            }
        }

        public void LoadDefaultSetting()
        {
            int randResult = new Random().Next(1, 1000000);
            FeedId = randResult;
            Alias = "RssTerminal" + randResult;
            FeedName = "RSS输出-" + randResult;
            Interval = 1200000;
        }

        [STKDescription("输出RSS信息")]
        public void DisplayRss(HttpListenerContext context,string subUrl)
        {
            feed.Links.Clear();
            feed.Links.Add(SyndicationLink.CreateSelfLink(context.Request.Url));//.BaseUri = context.Request.Url;
            using (StreamWriter writer = new StreamWriter(context.Response.OutputStream))
            {
                XmlWriter xmlWriter = XmlWriter.Create(writer);
                feed.SaveAsAtom10(xmlWriter);
                xmlWriter.Close();
                context.Response.Close();
            }
        }

        
    }
}
