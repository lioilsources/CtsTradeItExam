﻿using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using CTSTestApplication;

namespace CtsTrades
{
    [XmlRoot("TradesList", Namespace="http://www.cts-tradeit.com")]
    public class XmlTradeList
    {
        [XmlElement("Trade")]
        public XmlTrade[] Trades { get; set; }
    }

    public class XmlTrade
    {
        [XmlElement("Direction")]
        public string Direction { get; set; } // TODO
        [XmlElement("ISIN")]
        public string ISIN { get; set; }
        [XmlElement("Quantity")]
        public decimal Quantity { get; set; }
        [XmlElement("Price")]
        public decimal Price { get; set; }
    }

    static class XmlExtensions
    {
        public static string ElementValue(this XContainer container, XName name)
        {
            if (container == null)
                return null;
            var element = container.Element(name);
            return element != null ? element.Value : null;
        }

        public static Func<XmlReader, T> Deserializer<T>()
        {
            var serializer = new XmlSerializer(typeof(T));
            return reader => (T)serializer.Deserialize(reader);
        }
    }

    static class CollectionEx
    {
        public static void Each<T>(this IEnumerable<T> items, Action<T> action)
        {
            foreach (var item in items)
            {
                action(item);
            }
        }

        public static void Each<T>(this IEnumerable<T> items, Action<T, int> action)
        {
            var i = 0;
            foreach (var item in items)
            {
                action(item, i++);
            }
        }

        public static IEnumerable<List<T>> Grouped<T>(this IEnumerable<T> items, int groupSize)
        {
            using (var enumerator = items.GetEnumerator())
            {
                while (true)
                {
                    var list = new List<T>(groupSize);
                    while (list.Count < groupSize)
                    {
                        if (!enumerator.MoveNext())
                        {
                            if (list.Count > 0)
                                yield return list;
                            yield break;
                        }
                        list.Add(enumerator.Current);
                    }
                    yield return list;
                }
            }
        }

        public static Dictionary<K, List<T>> ToDictionaryMany<T, K>(this IEnumerable<T> items, Func<T, K> keySelector)
        {
            var result = new Dictionary<K, List<T>>();
            foreach (var item in items)
            {
                var key = keySelector(item);
                if (key != null)
                    result.Obtain(key).Add(item);
            }
            return result;
        }
    }

    static class DictionaryEx
    {
        public static V Obtain<K, V>(this IDictionary<K, V> map, K key)
            where V : new()
        {
            V value;
            if (!map.TryGetValue(key, out value))
            {
                value = new V();
                map.Add(key, value);
            }
            return value;
        }
    }

    static class DebugHelper
    {
        public static void MeasureTime(string message, Action action)
        {
            Console.WriteLine($"{message}");
            Stopwatch stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            Console.WriteLine($"... elapsed {stopwatch.Elapsed}");
        }
    }

    class MainClass
    {
        static readonly string fileName = "TradesList.xml";

        static readonly int numberOfTrades = 50000;
        static readonly int groupBy = 9;
        static readonly int numberOfRetries = 10;

        static IEnumerable<XmlTrade> CustomDeserialize(XElement root)
        {
            var xmlns = "http://www.cts-tradeit.com";

            var tradesElement = XName.Get("TradesList", xmlns);
            var tradeElement = XName.Get("Trade", xmlns);
            var isinElement = XName.Get("ISIN", xmlns);
            var directionElement = XName.Get("Direction", xmlns);
            var quantityElement = XName.Get("Quantity", xmlns);
            var priceElement = XName.Get("Price", xmlns);
           
            return root.Elements(tradeElement).Select(t => new XmlTrade()
            {
                ISIN = t.ElementValue(isinElement),
                Direction = t.ElementValue(directionElement),
                Quantity = Decimal.Parse(t.ElementValue(quantityElement)),
                Price = Decimal.Parse(t.ElementValue(priceElement))
            });
        }

        static IEnumerable<XmlTrade> ApiDeserialize(string data)
        {
            var deserialize = XmlExtensions.Deserializer<XmlTradeList>();
            using (var reader = new XmlTextReader(new StringReader(data)))
            {
                return deserialize(reader).Trades;
            }
        }

        class BestTrade
        {
            public string ISIN { get; set; }
            public int TradesCount { get; set; }
            public decimal Sum { get; set; }
        }

        static void BestTrades(string direction, IEnumerable<XmlTrade> trades,
                        Func<IEnumerable<XmlTrade>, Func<XmlTrade, decimal>, IEnumerable<XmlTrade>> orderByTrades,
                        Func<IEnumerable<BestTrade>, Func<BestTrade, decimal>, IEnumerable<BestTrade>> orderByBestTrades)
        {
            var bestBuys = trades.Where(t => t.Direction == direction).ToDictionaryMany(t => t.ISIN).Select(t => new BestTrade
            {
                ISIN = t.Key,
                TradesCount = t.Value.Count(),
                Sum = orderByTrades(t.Value, trade => trade.Price).Take(10).Sum(trade => trade.Price)
            });
            orderByBestTrades(bestBuys, _ => _.Sum).Take(3).Each(_ => Console.WriteLine($"{_.ISIN}: {_.Sum}/{_.TradesCount}"));

        }

        public static void Main(string[] args)
        {
            var currentPath = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            Console.WriteLine($"Current path {currentPath}.");
            var pathInput = Path.Combine(currentPath, "../../Data");
            Console.WriteLine($"Looking for source files in {new DirectoryInfo(pathInput).FullName}.");

            TextWriterTraceListener tl = new TextWriterTraceListener(System.Console.Out);
            Debug.Listeners.Add(tl);

            DebugHelper.MeasureTime($"Creating test file of {numberOfTrades}", () =>
            {
                new Tester().CreateTestFile(pathInput, numberOfTrades);
            });

            string data = null;
            DebugHelper.MeasureTime("Reading file", () =>
            {
                data = File.ReadAllText(Path.Combine(pathInput, fileName));
            });

            // Worse performance than Custom deserialization but less code
            //IEnumerable<XmlTrade> trades2 = null;
            //DebugHelper.MeasureTime("Deserializing API", () =>
            //{
            //    trades2 = ApiDeserialize(data);
            //});

            IEnumerable<XmlTrade> trades = null;
            DebugHelper.MeasureTime("Parsing XML + Custom deserializing", () =>
            {
                var document = XDocument.Parse(data);            
                trades = CustomDeserialize(document.Root);
            });

            var pathOutput = Path.Combine(currentPath, "../../Output");
            var adapter = new DataAdapter(pathOutput);
            DebugHelper.MeasureTime("PROCESS DATABASE", () =>
            {
                var globalRetries = 0;
                trades.Grouped(groupBy).Each((tradesGroup, index) =>
                {
                    var transactionName = $"no {index.ToString()}";

                    var failOrRetry = true;
                    var retry = 0;
                    while (failOrRetry)
                    {
                        adapter.BeginTransaction(transactionName);
                        try
                        {
                            tradesGroup.Each(trade =>
                                adapter.Process(Operation.Insert, "INSERT INTO dbo.Trades(ISIN, Quantity, Price, Direction) " +
                                                $"VALUES({trade.ISIN}, {trade.Quantity}, {trade.Price}, {trade.Direction});"));
                            adapter.CommitTransaction(transactionName);
                            if (retry == 0)
                            {
                                Debug.WriteLine($"Successful transaction {transactionName}.");
                            }
                            else
                            {
                                Debug.WriteLine($"Successful transaction {transactionName} on {retry}. retry.");
                            }

                            failOrRetry = false;
                        }
                        catch (Exception e)
                        {
                            adapter.RollbackTransaction(transactionName);
                            Debug.WriteLine($"Exception {e.Message} in transaction {transactionName}/{retry}.");

                            retry++;
                            globalRetries++;
                            if (retry >= numberOfRetries)
                            {
                                failOrRetry = false;
                                Console.WriteLine($"Uncompleted transaction {transactionName}.");
                            }
                        }
                    }
                });

                Debug.Flush();
                Console.WriteLine($"Transactions/No of retries/Operations: {numberOfTrades / groupBy}/{globalRetries}/{numberOfTrades / groupBy + globalRetries}");
            });

            // best trades
            DebugHelper.MeasureTime("Best BUYS / from lower", () =>
            {
                 BestTrades("B", trades, 
                           (buys, orderBySelector) => buys.OrderBy(orderBySelector),
                           (bestTrades, orderBySelector) => bestTrades.OrderBy(orderBySelector));
            });

            DebugHelper.MeasureTime("Best SELS / from higher", () =>
            {
                BestTrades("S", trades,
                           (sels, orderBySelector) => sels.OrderByDescending(orderBySelector),
                           (bestTrades, orderBySelector) => bestTrades.OrderByDescending(orderBySelector));
            });
        }
    }
}
