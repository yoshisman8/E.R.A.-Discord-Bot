using System;
using System.Net;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using SAIL.Classes;
using LiteDB;
using Discord.Addons.CommandCache;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Rest;
using Discord;

namespace SAIL.Modules
{
    [Name("Debugger Module")][Exclude]
    public class Debugger : InteractiveBase<SocketCommandContext>
    {
        public LiteDatabase Database {get;set;}
        public CommandCacheService CommandCache {get;set;}
        public IServiceProvider Provider {get;set;}
        public CommandService command {get;set;}
        private Controller Controller {get;set;} = new Controller();

        [Command("TestCharacter"),Alias("TChar")] [RequireOwner]
        [RequireContext(ContextType.Guild)]
        [Summary("[DEBUGGER COMMAND] Find a character from this server using their name. If multiple characters are found, only the first one (ordered alphabetically) will be displayed.")]
        public async Task GetCharacter([Remainder] string Name)
        {
            var All = Database.GetCollection<Character>("Characters").FindAll();
            
            if (All.Count() == 0)
            {
                var msg = await ReplyAsync("Character count is 0.");
                CommandCache.Add(Context.Message.Id,msg.Id);
                return;
            }

            var results = All.Where(x=>x.Name.ToLower().Contains(Name.ToLower()));
            if(results.Count() == 0)
            {
                var msg = await ReplyAsync("There are no characters whose names contain \""+Name+"\".");
                CommandCache.Add(Context.Message.Id,msg.Id);
                return;
            }
            else
            {
                var msg = await ReplyAsync("Searching for \""+Name+"\"...");

                var prev = new Emoji("⏮");
                await msg.AddReactionAsync(prev);
                var kill = new Emoji("⏹");
                await msg.AddReactionAsync(kill);
                var next = new Emoji("⏭");
                await msg.AddReactionAsync(next);

                var character = results.OrderBy(x=>x.Name).FirstOrDefault();
                Controller.Pages.Clear();
                Controller.Pages = character.PagesToEmbed();
                await msg.ModifyAsync(x=> x.Embed = Controller.Pages.ElementAt(Controller.Index));

                Interactive.AddReactionCallback(msg,new InlineReactionCallback(Interactive,Context,
                new ReactionCallbackData("",null,false,false,TimeSpan.FromMinutes(3))
                    .WithCallback(prev,(ctx,rea)=>Controller.Previous(ctx,rea,msg))
                    .WithCallback(kill,(ctx,rea)=>Controller.Kill(Interactive,msg))
                    .WithCallback(next,(ctx,rea)=>Controller.Next(ctx,rea,msg))));
                CommandCache.Add(Context.Message.Id,msg.Id);
            }
        }
        [Command("TQuote"),Alias("TQ")] [RequireOwner]
        [Summary("[DEBUGGER COMMAND] Fetches a random quote in this server.")]
        [RequireContext(ContextType.Guild)]
        public async Task RandomQuote()
        {
            var All = Database.GetCollection<Quote>("Quotes").FindAll();
            if (All.Count() == 0)
            {
                var msg = await ReplyAsync("This server has no recorded quotes. React with 📌 on a message said by someone on the server to add the first quote.");
                CommandCache.Add(Context.Message.Id,msg.Id);
                return;
            }
            var rnd = new Random().Next(0,All.Count()-1);
            
            var Quote = All.ElementAt(rnd);
            try{
                await Quote.GenerateContext(Context);
                var emb = StaticMethods.EmbedMessage(Context,Quote.Context.Channel,Quote.Context.Message);
                var emote = new Emoji("❓");

                var msg = await ReplyAsync("",embed: emb);
                
                CommandCache.Add(Context.Message.Id,msg.Id);
                var callback = new ReactionCallbackData("",emb,false,false,TimeSpan.FromMinutes(3));
                callback.WithCallback(emote, async (C,R) => await GetContext(Context,R,msg,Interactive,Quote,callback));
                Interactive.AddReactionCallback(msg,new InlineReactionCallback(Interactive,Context,callback));
                await msg.AddReactionAsync(emote);
            }
            catch (Exception e)
            {
                Database.GetCollection<Quote>("Quotes").Delete(x => x.Message == Quote.Message);
                var msg = await ReplyAsync("It seems like this quote has returned the error `"+e.Message+"` and has beed deleted from the databanks, Appologies!");
                CommandCache.Add(Context.Message.Id,msg.Id);
            }
        }
        [Command("TQuote"),Alias("TQ")] [RequireOwner]
        [Summary("[DEBUGGER COMMAND] Searches for a quote whose message contents contain a string of text. This is not case sensitive.")]
        [Priority(0)] [RequireContext(ContextType.Guild)]
        public async Task SearchQuoteText([Remainder] string Query)
        {
            var col = Database.GetCollection<Quote>("Quotes").FindAll();
            if (col.Count() == 0)
            {
                var msg = await ReplyAsync("This server has no recorded quotes. React with 📌 on a message said by someone on the server to add the first quote.");
                CommandCache.Add(Context.Message.Id,msg.Id);
                return;
            }
            var results = col.Where(x => x.SearchText.ToLower().Contains(Query.ToLower()));
            if (results.Count() == 0) 
            {
                var msg = await ReplyAsync("There are no quotes that contain the text \""+Query+"\".");
                CommandCache.Add(Context.Message.Id,msg.Id);
            }
            else
            {
                var msg = await ReplyAsync("Searching for \""+Query+"\"...");
                if(results.Count() > 1)
                {
                    var prev = new Emoji("⏮");
                    await msg.AddReactionAsync(prev);
                    var kill = new Emoji("⏹");
                    await msg.AddReactionAsync(kill);
                    var next = new Emoji("⏭");
                    await msg.AddReactionAsync(next);
                    Controller.Pages.Clear();
                    foreach(var x in results)
                    {
                        await x.GenerateContext(Context);
                        Controller.Pages.Add(StaticMethods.EmbedMessage(Context,x.Context.Channel,x.Context.Message));
                    }
                    await msg.ModifyAsync(x=>x.Content= "Found "+results.Count()+" results for '"+Query+"'.");
                    await msg.ModifyAsync(x=> x.Embed = Controller.Pages.ElementAt(Controller.Index));

                    Interactive.AddReactionCallback(msg,new InlineReactionCallback(Interactive,Context,
                    new ReactionCallbackData("",null,false,false,TimeSpan.FromMinutes(3))
                            .WithCallback(prev,(ctx,rea)=>Controller.Previous(ctx,rea,msg))
                            .WithCallback(kill,(ctx,rea)=>Controller.Kill(Interactive,msg))
                            .WithCallback(next,(ctx,rea)=>Controller.Next(ctx,rea,msg))));
                    CommandCache.Add(Context.Message.Id,msg.Id);
                }
                else
                {
                    var Q = results.FirstOrDefault();
                    try
                    {
                        await Q.GenerateContext(Context);
                        await msg.ModifyAsync(x=>x.Content = "Found one result for '"+Query+"'.");
                        var embed = StaticMethods.EmbedMessage(Context,Q.Context.Channel,Q.Context.Message);
                        await msg.ModifyAsync(x=>x.Embed = embed);
                        CommandCache.Add(Context.Message.Id,msg.Id);
                    }
                    catch (Exception e)
                    {
                        Database.GetCollection<Quote>("Quotes").Delete(x => x.Message == Q.Message);
                        await msg.ModifyAsync(x=>x.Content = "It seems like this quote has returned the error `"+e.Message+"` and has beed deleted from the databanks, Appologies!");
                        CommandCache.Add(Context.Message.Id,msg.Id);
                    }
                }
            }
        }
        [Command("Statistics")] [RequireOwner]
        public async Task Stats()
        {
            var guilds = Database.GetCollection<SysGuild>("Guilds").FindAll();
            var col = Database.GetCollection<Quote>("Quotes").FindAll();
            var All = Database.GetCollection<Character>("Characters").FindAll();

            var embed = new EmbedBuilder();
            foreach(var x in guilds)
            {
                embed.AddField(Context.Client.GetGuild(x.Id).Name,"Characters in this server: "+All.Where(c=>c.Guild==x.Id).Count()+"\n"+"Quotes in this server: "+col.Where(c=>c.Guild==x.Id).Count());
            }
            await ReplyAsync("",false,embed.Build());
        }
        [Command("ResetSettings")] [RequireOwner]
        public async Task Resetto()
        {
            var guilds = Database.GetCollection<SysGuild>("Guilds");
            foreach (var x in guilds.FindAll())
            {
                var mds = new Dictionary<ModuleInfo,bool>();
                foreach(var y in command.Modules)
                {
                    if(y.Name.ToLower().Contains("debug")) continue;
                    mds.Add(y,true);
                }
                x.Modules = mds;
                guilds.Update(x);
            }
            await ReplyAsync("Reset all guild module settings.");
        }
        public async Task GetContext(SocketCommandContext c, SocketReaction r, IUserMessage msg, InteractiveService interactive, Quote quote, ReactionCallbackData callback)
        {
            await msg.RemoveAllReactionsAsync();

            var prev = new Emoji("⏮");
            await msg.AddReactionAsync(prev);
            var kill = new Emoji("⏹");
            await msg.AddReactionAsync(kill);
            var next = new Emoji("⏭");
            await msg.AddReactionAsync(next);

            await quote.GenerateContext(c);
            var raw = await quote.Context.Channel.GetMessagesAsync(quote.Context.Message.Id,Direction.Before,5).FlattenAsync();
            var context = raw.OfType<IUserMessage>().OrderBy(x=>x.Timestamp);
            Controller.Pages.Clear();
            foreach(var x in context)
            {
                Controller.Pages.Add(StaticMethods.EmbedMessage(c,quote.Context.Channel,x));
            }
            Controller.Pages.Add(StaticMethods.EmbedMessage(c,quote.Context.Channel,quote.Context.Message));

            await msg.ModifyAsync(x=> x.Embed = Controller.Pages.ElementAt(Controller.Index));
            await msg.ModifyAsync(x=> x.Content = "Showing the last 5 messages before this Quote.\n"+
                "Use ⏮ and ⏭ to navigate. Press ⏹ to end navigation\n"+
                "Note: Navigation will be automatically dissabled after 3 minutes");
            
            callback.WithCallback(prev,(ctx,rea)=>Controller.Previous(c,rea,msg))
                .WithCallback(kill,(ctx,rea)=>Controller.Kill(interactive,msg))
                .WithCallback(next,(ctx,rea)=>Controller.Next(c,rea,msg));
            
            interactive.AddReactionCallback(msg,new InlineReactionCallback(Interactive,c,callback));
        }
    }
}