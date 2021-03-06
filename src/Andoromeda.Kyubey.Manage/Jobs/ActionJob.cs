﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Pomelo.AspNetCore.TimedJob;
using Andoromeda.Kyubey.Models;
using Andoromeda.Kyubey.Manage.Models;

namespace Andoromeda.Kyubey.Manage.Jobs
{
    public class ActionJob : Job
    {
        [Invoke(Begin = "2018-06-01", Interval = 1000 * 5, SkipWhileExecuting = true)]
        public void PollActions(IConfiguration config, KyubeyContext db)
        {
            TryHandleActionAsync(config, db).Wait();
        }
        
        private async Task TryHandleActionAsync(IConfiguration config, KyubeyContext db)
        {
            while(true)
            {
                var actions = await LookupActionAsync(config, db);
                foreach (var act in actions)
                {
                    Console.WriteLine($"Handling action log {act.account_action_seq} {act.action_trace.act.name}");
                    var blockTime = TimeZoneInfo.ConvertTimeToUtc(Convert.ToDateTime(act.block_time + 'Z'));
                    switch (act.action_trace.act.name)
                    {
                        case "sellmatch":
                            await HandleSellMatchAsync(db, act.action_trace.act.data, blockTime);
                            break;
                        case "buymatch":
                            await HandleBuyMatchAsync(db, act.action_trace.act.data, blockTime);
                            break;
                        case "sellreceipt":
                            await HandleSellReceiptAsync(db, act.action_trace.act.data, blockTime);
                            break;
                        case "buyreceipt":
                            await HandleBuyReceiptAsync(db, act.action_trace.act.data, blockTime);
                            break;
                        case "cancelbuy":
                            await HandleCancelBuyAsync(db, act.action_trace.act.data, blockTime);
                            break;
                        case "cancelsell":
                            await HandleCancelSellAsync(db, act.action_trace.act.data, blockTime);
                            break;
                        default:
                            continue;
                    }
                }

                if (actions.Count() < 20)
                {
                    break;
                }
            }
        }

        private async Task HandleCancelSellAsync(KyubeyContext db, ActionDataWrap data, DateTime time)
        {
            try
            {
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == data.id && x.TokenId == data.symbol);
                if (order != null)
                {
                    db.DexSellOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task HandleCancelBuyAsync(KyubeyContext db, ActionDataWrap data, DateTime time)
        {
            try
            {
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == data.id && x.TokenId == data.symbol);
                if (order != null)
                {
                    db.DexBuyOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task HandleSellReceiptAsync(KyubeyContext db, ActionDataWrap data, DateTime time)
        {
            try
            {
                var token = data.data.bid.Split(' ')[1];
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == data.data.id && x.TokenId == token);
                if (order != null)
                {
                    db.DexSellOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
                order = new DexSellOrder
                {
                    Id = data.data.id,
                    Account = data.data.account,
                    Ask = Convert.ToDouble(data.data.ask.Split(' ')[0]),
                    Bid = Convert.ToDouble(data.data.bid.Split(' ')[0]),
                    UnitPrice = data.data.unit_price / 10000.0,
                    Time = time,
                    TokenId = token
                };
                db.DexSellOrders.Add(order);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task HandleBuyReceiptAsync(KyubeyContext db, ActionDataWrap data, DateTime time)
        {
            try
            {
                var token = data.data.ask.Split(' ')[1];
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == data.data.id && x.TokenId == token);
                if (order != null)
                {
                    db.DexBuyOrders.Remove(order);
                    await db.SaveChangesAsync();
                }
                order = new DexBuyOrder
                {
                    Id = data.data.id,
                    Account = data.data.account,
                    Ask = Convert.ToDouble(data.data.ask.Split(' ')[0]),
                    Bid = Convert.ToDouble(data.data.bid.Split(' ')[0]),
                    UnitPrice = data.data.unit_price / 10000.0,
                    Time = time,
                    TokenId = token
                };
                db.DexBuyOrders.Add(order);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task HandleSellMatchAsync(KyubeyContext db, ActionDataWrap data, DateTime time)
        {
            try
            {
                var token = data.data.bid.Split(' ')[1];
                var bid = Convert.ToDouble(data.data.bid.Split(' ')[0]);
                var ask = Convert.ToDouble(data.data.ask.Split(' ')[0]);
                var order = await db.DexBuyOrders.SingleOrDefaultAsync(x => x.Id == data.data.id && x.TokenId == token);
                if (order != null)
                {
                    order.Bid -= bid;
                    order.Ask -= ask;
                    if (order.Ask <= 0 || order.Bid <= 0)
                    {
                        db.DexBuyOrders.Remove(order);
                    }
                    await db.SaveChangesAsync();
                }
                db.MatchReceipts.Add(new MatchReceipt
                {
                    Ask = ask,
                    Bid = bid,
                    Asker = data.data.asker,
                    Bidder = data.data.bidder,
                    Time = time,
                    TokenId = token,
                    UnitPrice = data.data.unit_price / 10000.0
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task HandleBuyMatchAsync(KyubeyContext db, ActionDataWrap data, DateTime time)
        {
            try
            {
                var token = data.data.ask.Split(' ')[1];
                var bid = Convert.ToDouble(data.data.bid.Split(' ')[0]);
                var ask = Convert.ToDouble(data.data.ask.Split(' ')[0]);
                var order = await db.DexSellOrders.SingleOrDefaultAsync(x => x.Id == data.data.id && x.TokenId == token);
                if (order != null)
                {
                    order.Bid -= bid;
                    order.Ask -= ask;
                    if (order.Ask <= 0 || order.Bid <= 0)
                    {
                        db.DexSellOrders.Remove(order);
                    }
                    await db.SaveChangesAsync();
                }
                db.MatchReceipts.Add(new MatchReceipt
                {
                    Ask = ask,
                    Bid = bid,
                    Asker = data.data.asker,
                    Bidder = data.data.bidder,
                    Time = time,
                    TokenId = token,
                    UnitPrice = data.data.unit_price / 10000.0
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private async Task<IEnumerable<EosAction>> LookupActionAsync(IConfiguration config, KyubeyContext db)
        {
            var row = await db.Constants.SingleAsync(x => x.Id == "action_pos");
            var position = Convert.ToInt64(row.Value);
            using (var client = new HttpClient { BaseAddress = new Uri(config["TransactionNodeBackup"]) })
            using (var response = await client.PostAsJsonAsync("/v1/history/get_actions", new
            {
                account_name = "kyubeydex.bp",
	            pos = position,
	            offset = 100
            }))
            {
                var txt = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<EosActionWrap>(txt, new JsonSerializerSettings
                {
                    Error = HandleDeserializationError
                });
                if (result.actions.Count() == 0)
                {
                    return null;
                }
                if (result.actions.Count() > 0)
                {
                    row.Value = result.actions.Last().account_action_seq.ToString();
                    await db.SaveChangesAsync();
                }

                return result.actions;
            }
        }

        private void HandleDeserializationError(object sender, ErrorEventArgs errorArgs)
        {
            var currentError = errorArgs.ErrorContext.Error.Message;
            errorArgs.ErrorContext.Handled = true;
        }
    }
}
