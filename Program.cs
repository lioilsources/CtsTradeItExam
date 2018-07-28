using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using CTSTestApplication;

namespace CtsTrades
{
    enum TradeType { B, S }

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
            Stopwatch stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            Console.WriteLine($"{message} elapsed {stopwatch.Elapsed}");
        }
    }

    class MainClass
    {
        static readonly string path = "/Users/odrichvorechovskyjr/Downloads/Zadanie";
        static readonly string fileName = "TradesList.xml";

        static readonly int numberOfTrades = 1000;
        static readonly int groupBy = 21;
        static readonly int numberOfRetries = 3;

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

        public static void Main(string[] args)
        {
            DebugHelper.MeasureTime($"Creating test file of {numberOfTrades}", () =>
            {
                new Tester().CreateTestFile(path, numberOfTrades);
            });

            string data = null;
            DebugHelper.MeasureTime("Reading file", () =>
            {
                data = File.ReadAllText(Path.Combine(path, fileName));
            });

            XDocument document = null;
            DebugHelper.MeasureTime("Parsing XML", () =>
            {
                document = XDocument.Parse(data);
            });

            var root = document.Root;

            IEnumerable<XmlTrade> trades1 = null;
            DebugHelper.MeasureTime("Custom deserializing", () =>
            {
                trades1 = CustomDeserialize(root);
                Console.WriteLine($"End of World! {trades1.First().ISIN} {trades1.Count()}");
            });

            XmlTrade[] trades2 = null;
            DebugHelper.MeasureTime("Deserializing API", () =>
            {
                
                var deserialize = XmlExtensions.Deserializer<XmlTradeList>();
                using (var reader = new XmlTextReader(new StringReader(data)))
                {
                    trades2 = deserialize(reader).Trades;
                }
                Console.WriteLine($"End of World! {trades2.First().ISIN} {trades2.Count()}");
            });

            var adapter = new DataAdapter(path);
            DebugHelper.MeasureTime("PROCESS DATABASE", () =>
            {
                trades2.Grouped(groupBy).Each((trades, index) =>
                {
                    var transactionName = index.ToString();

                    var failOrRetry = true;
                    var retry = 0;
                    while (failOrRetry)
                    {
                        adapter.BeginTransaction(transactionName);
                        try
                        {
                            trades.Each(trade =>
                            {
                                adapter.Process(Operation.Insert, "INSERT INTO dbo.Trades(ISIN, Quantity, Price, Direction)" +
                                                "VALUES(@1, @2, @3, @4);", trade.ISIN, trade.Quantity, trade.Price, trade.Direction);
                            });
                            adapter.CommitTransaction(transactionName);
                            //if (retry == 0)
                            //{
                            //    Console.WriteLine($"Successful transaction {transactionName}.");
                            //}
                            //else
                            //{
                            //    Console.WriteLine($"Successful transaction {transactionName} on {retry}. retry.");
                            //}

                            failOrRetry = false;
                        }
                        catch (Exception e)
                        {
                            adapter.RollbackTransaction(transactionName);
                            //Console.WriteLine($"Exception {e.Message} in transaction {transactionName}/{retry}.");

                            retry++;
                            if (retry > numberOfRetries)
                            {
                                failOrRetry = false;
                                Console.WriteLine($"Uncompleted transaction {transactionName}.");
                            }
                        }
                        finally
                        {
                            //adapter.EndTransaction(transactionName);
                        }
                    }
                });
            });

            // best trades
            DebugHelper.MeasureTime("Best BUYS / from lower", () =>
            {
                 BestTrades("B", trades1, 
                           (trades, orderBySelector) => trades.OrderBy(orderBySelector),
                           (bestTrades, orderBySelector) => bestTrades.OrderBy(orderBySelector));
            });

            DebugHelper.MeasureTime("Best SELS / from higher", () =>
            {
                BestTrades("S", trades2,
                           (trades, orderBySelector) => trades.OrderByDescending(orderBySelector),
                           (bestTrades, orderBySelector) => bestTrades.OrderByDescending(orderBySelector));
            });
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
            var bestBuys = trades.Where(t => t.Direction == "B").ToDictionaryMany(t => t.ISIN).Select(t => new BestTrade
            {
                ISIN = t.Key,
                TradesCount = t.Value.Count(),
                Sum = orderByTrades(t.Value, trade => trade.Price).Take(10).Sum(trade => trade.Price)
            });
            orderByBestTrades(bestBuys, _ => _.Sum).Take(3).Each(_ => Console.WriteLine($"{_.ISIN}: {_.Sum}/{_.TradesCount}"));

        }
    }
}
