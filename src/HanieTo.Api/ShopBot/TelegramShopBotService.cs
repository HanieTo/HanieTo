using System.Collections.Concurrent;
using System.Globalization;
using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace HanieTo.Api.ShopBot;

// Interactive Telegram shop bot: browse the catalog and order, in a back-and-forth
// conversation with each customer (distinct from Publishing/.../TelegramPublisher,
// which only pushes one-way channel announcements).
//
// Built on the Telegram.Bot library (typed client + keyboards + robust polling)
// rather than hand-rolled HTTP/JSON. DropPendingUpdates=true means a restart does
// NOT replay the backlog of old taps.
//
// Keyboard model (the user wanted both):
// - A persistent reply keyboard docked at the bottom = the main menu
//   (Products / Cart / Orders / Help / Language), always visible.
// - Inline keyboards on a single reused message = drill-down navigation, edited in
//   place so menus never stack.
// - Typing a non-menu message shows one short "use the menu" hint that never stacks.
public class TelegramShopBotService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramShopBotService> logger) : BackgroundService
{
    // The single live inline "menu" message per chat, edited in place.
    private readonly ConcurrentDictionary<long, int> _lastMenu = new();
    // The single "use the menu" hint per chat, so stray text never stacks.
    private readonly ConcurrentDictionary<long, int> _hint = new();

    // Maps a bottom-bar button's text (in every language) back to its action.
    private static readonly Dictionary<string, string> BarActions = BuildBarActions();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = configuration["ShopBot:TelegramBotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("ShopBot:TelegramBotToken is not configured - shop bot is disabled.");
            return;
        }

        var bot = new TelegramBotClient(botToken);
        var options = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
            DropPendingUpdates = true
        };

        logger.LogInformation("Shop bot started, polling for updates.");
        await bot.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync, options, stoppingToken);
    }

    private Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Shop bot polling error");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (update.Message is { } message)
            {
                await HandleMessageAsync(bot, db, message, ct);
            }
            else if (update.CallbackQuery is { } callback)
            {
                await HandleCallbackAsync(bot, db, callback, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling shop bot update");
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient bot, AppDbContext db, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = (message.Text ?? "").Trim();

        var pref = await db.ChatPreferences.FirstOrDefaultAsync(p => p.ChatId == chatId.ToString(), ct);

        // First contact OR /start: dock the bottom bar immediately (default English if
        // no language chosen yet) and greet, so the menu is visible from the very first
        // screen. Language can be changed any time via the 🌐 button.
        if (pref is null || text.StartsWith('/'))
        {
            var startLang = pref?.Language ?? BotLanguage.English;
            await DockBarAndWelcomeAsync(bot, chatId, startLang, ct);
            return;
        }

        var lang = pref.Language;

        // A tap on the bottom bar arrives as text matching a button label.
        if (BarActions.TryGetValue(text, out var action))
        {
            await ClearHintAsync(bot, chatId, ct);
            await RouteAsync(bot, db, chatId, null, action, ct);
            return;
        }

        // Anything else: one non-stacking hint, not a new menu.
        await ShowHintAsync(bot, chatId, lang, ct);
    }

    private async Task HandleCallbackAsync(ITelegramBotClient bot, AppDbContext db, CallbackQuery callback, CancellationToken ct)
    {
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        if (callback.Message is null)
        {
            return;
        }

        var chatId = callback.Message.Chat.Id;
        _lastMenu[chatId] = callback.Message.MessageId;
        await RouteAsync(bot, db, chatId, callback.Message.MessageId, callback.Data ?? "", ct);
    }

    // messageId non-null => a button tap, edit that message in place.
    // messageId null     => triggered by the bottom bar, render a fresh menu.
    private async Task RouteAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, string data, CancellationToken ct)
    {
        var parts = data.Split(':');
        var action = parts.ElementAtOrDefault(0);

        if (action == "lang")
        {
            await SetLanguageAsync(bot, db, chatId, parts.ElementAtOrDefault(1) ?? "en", ct);
            return;
        }

        var lang = await GetLanguageAsync(db, chatId, ct);

        switch (action)
        {
            case "home":
                await RenderAsync(bot, chatId, messageId, Loc(lang, T.WelcomeMenu), MainMenuKeyboard(lang), ct);
                break;
            case "language":
                await RenderAsync(bot, chatId, messageId, Loc(lang, T.ChooseLanguage), LanguageKeyboard(), ct);
                break;
            case "products":
                await ShowCategoriesAsync(bot, db, chatId, messageId, lang, ct);
                break;
            case "cat":
                await ShowProductsAsync(bot, db, chatId, messageId, lang, parts.ElementAtOrDefault(1) ?? "", ct);
                break;
            case "prod":
                await ShowProductAsync(bot, db, chatId, messageId, lang, Guid.Parse(parts[1]), ct);
                break;
            case "qty":
                await ShowQuantityAsync(bot, db, chatId, messageId, lang, Guid.Parse(parts[1]), ct);
                break;
            case "addcart":
                await AddToCartAsync(bot, db, chatId, messageId, lang, Guid.Parse(parts[1]), int.Parse(parts[2]), ct);
                break;
            case "cart":
                await ShowCartAsync(bot, db, chatId, messageId, lang, ct);
                break;
            case "checkout":
                await CheckoutAsync(bot, db, chatId, messageId, lang, ct);
                break;
            case "clearcart":
                await ClearCartAsync(bot, db, chatId, messageId, lang, ct);
                break;
            case "orders":
                await ShowOrdersAsync(bot, db, chatId, messageId, lang, ct);
                break;
            case "help":
                await RenderAsync(bot, chatId, messageId, Loc(lang, T.HelpText), BackHomeKeyboard(lang, "home"), ct);
                break;
            default:
                await RenderAsync(bot, chatId, messageId, Loc(lang, T.WelcomeMenu), MainMenuKeyboard(lang), ct);
                break;
        }
    }

    private async Task SetLanguageAsync(ITelegramBotClient bot, AppDbContext db, long chatId, string code, CancellationToken ct)
    {
        var lang = code switch
        {
            "ru" => BotLanguage.Russian,
            "fa" => BotLanguage.Persian,
            _ => BotLanguage.English
        };

        var pref = await db.ChatPreferences.FirstOrDefaultAsync(p => p.ChatId == chatId.ToString(), ct);
        if (pref is null)
        {
            db.ChatPreferences.Add(new ChatPreference { ChatId = chatId.ToString(), Language = lang });
        }
        else
        {
            pref.Language = lang;
        }
        await db.SaveChangesAsync(ct);

        await DockBarAndWelcomeAsync(bot, chatId, lang, ct);
    }

    private async Task<BotLanguage> GetLanguageAsync(AppDbContext db, long chatId, CancellationToken ct)
    {
        var pref = await db.ChatPreferences.FirstOrDefaultAsync(p => p.ChatId == chatId.ToString(), ct);
        return pref?.Language ?? BotLanguage.English;
    }

    private async Task ShowCategoriesAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, CancellationToken ct)
    {
        var categories = await db.Products
            .Where(p => p.IsActive && p.Category != null)
            .Select(p => p.Category!)
            .Distinct()
            .ToListAsync(ct);

        if (categories.Count == 0)
        {
            await ShowProductsAsync(bot, db, chatId, messageId, lang, "", ct);
            return;
        }

        var rows = categories.Select(c => new[] { InlineKeyboardButton.WithCallbackData($"📁 {c}", $"cat:{c}") }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home")]);

        await RenderAsync(bot, chatId, messageId, Loc(lang, T.Categories), new InlineKeyboardMarkup(rows), ct);
    }

    private async Task ShowProductsAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, string category, CancellationToken ct)
    {
        var query = db.Products.Where(p => p.IsActive);
        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(p => p.Category == category);
        }
        var products = await query.ToListAsync(ct);

        if (products.Count == 0)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.NoProducts), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var rows = products.Select(p =>
        {
            var label = p.Stock > 0
                ? $"{p.Name} — {Money(p.Price)}"
                : $"{p.Name} — {Loc(lang, T.OutOfStockShort)}";
            return new[] { InlineKeyboardButton.WithCallbackData(label, $"prod:{p.Id:N}") };
        }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData(Loc(lang, T.Back), "products"), InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home")]);

        var title = string.IsNullOrEmpty(category) ? Loc(lang, T.PickProduct, "🛍") : Loc(lang, T.PickProduct, category);
        await RenderAsync(bot, chatId, messageId, title, new InlineKeyboardMarkup(rows), ct);
    }

    private async Task ShowProductAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.ProductGone), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var text =
            $"🛍 *{product.Name}*\n\n" +
            (string.IsNullOrWhiteSpace(product.Description) ? "" : $"{product.Description}\n\n") +
            $"💰 {Loc(lang, T.ProductPrice)}: *{Money(product.Price)}*\n" +
            $"📦 {Loc(lang, T.ProductStock)}: {product.Stock}";

        var backTarget = product.Category is not null ? $"cat:{product.Category}" : "products";
        var rows = new List<InlineKeyboardButton[]>();
        if (product.Stock > 0)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData(Loc(lang, T.BuyButton), $"qty:{product.Id:N}")]);
        }
        rows.Add([InlineKeyboardButton.WithCallbackData(Loc(lang, T.Back), backTarget), InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home")]);

        await RenderAsync(bot, chatId, messageId, text, new InlineKeyboardMarkup(rows), ct);
    }

    private async Task ShowQuantityAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null || product.Stock <= 0)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.OutOfStock), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var max = Math.Min(product.Stock, 5);
        var qtyButtons = Enumerable.Range(1, max)
            .Select(n => InlineKeyboardButton.WithCallbackData(n.ToString(), $"addcart:{product.Id:N}:{n}"))
            .ToArray();

        List<InlineKeyboardButton[]> rows =
        [
            qtyButtons,
            [InlineKeyboardButton.WithCallbackData(Loc(lang, T.Back), $"prod:{product.Id:N}"), InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home")]
        ];

        await RenderAsync(bot, chatId, messageId, $"🛍 *{product.Name}*\n\n{Loc(lang, T.ChooseQuantity)}", new InlineKeyboardMarkup(rows), ct);
    }

    private async Task AddToCartAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, Guid productId, int quantity, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null || product.Stock < quantity)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.OutOfStock), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var existing = await db.CartItems.FirstOrDefaultAsync(c => c.ChatId == chatId.ToString() && c.ProductId == productId, ct);
        if (existing is null)
        {
            db.CartItems.Add(new CartItem { ChatId = chatId.ToString(), ProductId = productId, Quantity = quantity });
        }
        else
        {
            existing.Quantity += quantity;
        }
        await db.SaveChangesAsync(ct);

        List<InlineKeyboardButton[]> rows =
        [
            [InlineKeyboardButton.WithCallbackData(Loc(lang, T.ViewCart), "cart")],
            [InlineKeyboardButton.WithCallbackData(Loc(lang, T.ContinueShopping), "products"), InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home")]
        ];
        await RenderAsync(bot, chatId, messageId, $"{Loc(lang, T.AddedToCart)}\n\n🛍 {product.Name} × {quantity}", new InlineKeyboardMarkup(rows), ct);
    }

    private async Task ShowCartAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, CancellationToken ct)
    {
        var (lines, total, empty) = await BuildCartAsync(db, chatId, ct);
        if (empty)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.CartEmpty), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        var text = $"{Loc(lang, T.CartTitle)}\n\n{lines}\n\n*{Loc(lang, T.OrderTotal)}: {Money(total)}*";
        List<InlineKeyboardButton[]> rows =
        [
            [InlineKeyboardButton.WithCallbackData(Loc(lang, T.Checkout), "checkout")],
            [InlineKeyboardButton.WithCallbackData(Loc(lang, T.ClearCart), "clearcart"), InlineKeyboardButton.WithCallbackData(Loc(lang, T.ContinueShopping), "products")],
            [InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home")]
        ];
        await RenderAsync(bot, chatId, messageId, text, new InlineKeyboardMarkup(rows), ct);
    }

    private async Task<(string Lines, decimal Total, bool Empty)> BuildCartAsync(AppDbContext db, long chatId, CancellationToken ct)
    {
        var cart = await db.CartItems.Where(c => c.ChatId == chatId.ToString()).ToListAsync(ct);
        if (cart.Count == 0)
        {
            return ("", 0, true);
        }

        var productIds = cart.Select(c => c.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var lines = new List<string>();
        decimal total = 0;
        foreach (var item in cart)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }
            var lineTotal = product.Price * item.Quantity;
            total += lineTotal;
            lines.Add($"• {product.Name} × {item.Quantity} — {Money(lineTotal)}");
        }

        return (string.Join("\n", lines), total, lines.Count == 0);
    }

    private async Task CheckoutAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, CancellationToken ct)
    {
        var cart = await db.CartItems.Where(c => c.ChatId == chatId.ToString()).ToListAsync(ct);
        if (cart.Count == 0)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.CartEmpty), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        var productIds = cart.Select(c => c.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var order = new Order { BuyerChatId = chatId.ToString(), Status = OrderStatus.Confirmed };
        foreach (var item in cart)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }
            var qty = Math.Min(item.Quantity, product.Stock);
            if (qty <= 0)
            {
                continue;
            }
            product.Stock -= qty;
            order.Items.Add(new OrderItem { ProductId = product.Id, ProductName = product.Name, UnitPrice = product.Price, Quantity = qty });
        }

        if (order.Items.Count == 0)
        {
            db.CartItems.RemoveRange(cart);
            await db.SaveChangesAsync(ct);
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.OutOfStock), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        db.Orders.Add(order);
        db.CartItems.RemoveRange(cart);
        await db.SaveChangesAsync(ct);

        var summary = string.Join("\n", order.Items.Select(i => $"• {i.ProductName} × {i.Quantity} — {Money(i.UnitPrice * i.Quantity)}"));
        var text = Loc(lang, T.OrderPlaced, summary, Loc(lang, T.OrderTotal), Money(order.Total), Loc(lang, T.OrderId), order.Id.ToString("N"));
        await RenderAsync(bot, chatId, messageId, text, BackHomeKeyboard(lang, "home"), ct);

        await NotifyAdminAsync(bot, chatId, order, ct);
    }

    private async Task ClearCartAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, CancellationToken ct)
    {
        var cart = await db.CartItems.Where(c => c.ChatId == chatId.ToString()).ToListAsync(ct);
        db.CartItems.RemoveRange(cart);
        await db.SaveChangesAsync(ct);
        await RenderAsync(bot, chatId, messageId, Loc(lang, T.CartEmpty), BackHomeKeyboard(lang, "home"), ct);
    }

    private async Task ShowOrdersAsync(ITelegramBotClient bot, AppDbContext db, long chatId, int? messageId, BotLanguage lang, CancellationToken ct)
    {
        var orders = await db.Orders
            .Include(o => o.Items)
            .Where(o => o.BuyerChatId == chatId.ToString())
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            await RenderAsync(bot, chatId, messageId, Loc(lang, T.NoOrders), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        var text = Loc(lang, T.YourOrders) + "\n\n" + string.Join("\n\n", orders.Select(o =>
            $"🗓 {o.CreatedAtUtc:yyyy-MM-dd HH:mm}\n" +
            string.Join("\n", o.Items.Select(i => $"• {i.ProductName} × {i.Quantity} — {Money(i.UnitPrice * i.Quantity)}")) +
            $"\n*{Loc(lang, T.OrderTotal)}: {Money(o.Total)}*"));

        await RenderAsync(bot, chatId, messageId, text, BackHomeKeyboard(lang, "home"), ct);
    }

    private async Task NotifyAdminAsync(ITelegramBotClient bot, long buyerChatId, Order order, CancellationToken ct)
    {
        var adminChatId = configuration["ShopBot:AdminChatId"];
        if (string.IsNullOrWhiteSpace(adminChatId) || !long.TryParse(adminChatId, out var adminId))
        {
            return;
        }

        var summary = string.Join("\n", order.Items.Select(i => $"• {i.ProductName} × {i.Quantity} — {Money(i.UnitPrice * i.Quantity)}"));
        var text = $"🔔 *New order*\nFrom chat: `{buyerChatId}`\n\n{summary}\n\n*Total: {Money(order.Total)}*\nOrder ID: `{order.Id:N}`";
        try
        {
            await bot.SendMessage(adminId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin order notification");
        }
    }

    // --- welcome / hint ---

    private async Task DockBarAndWelcomeAsync(ITelegramBotClient bot, long chatId, BotLanguage lang, CancellationToken ct)
    {
        await ClearHintAsync(bot, chatId, ct);
        if (_lastMenu.TryRemove(chatId, out var oldId))
        {
            await DeleteMessageAsync(bot, chatId, oldId, ct);
        }

        var msg = await bot.SendMessage(chatId, Loc(lang, T.WelcomePrompt), parseMode: ParseMode.Markdown, replyMarkup: MainBarKeyboard(lang), cancellationToken: ct);
        _lastMenu[chatId] = msg.MessageId;
    }

    private async Task ShowHintAsync(ITelegramBotClient bot, long chatId, BotLanguage lang, CancellationToken ct)
    {
        var text = Loc(lang, T.UseMenu);
        if (_hint.TryGetValue(chatId, out var hintId) && await TryEditAsync(bot, chatId, hintId, text, null, ct))
        {
            return;
        }

        var msg = await bot.SendMessage(chatId, text, cancellationToken: ct);
        _hint[chatId] = msg.MessageId;
    }

    private async Task ClearHintAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        if (_hint.TryRemove(chatId, out var hintId))
        {
            await DeleteMessageAsync(bot, chatId, hintId, ct);
        }
    }

    // --- keyboards ---

    private static InlineKeyboardMarkup MainMenuKeyboard(BotLanguage lang) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData(Loc(lang, T.BrowseProducts), "products") },
        new[] { InlineKeyboardButton.WithCallbackData(Loc(lang, T.ViewCart), "cart"), InlineKeyboardButton.WithCallbackData(Loc(lang, T.MyOrders), "orders") },
        new[] { InlineKeyboardButton.WithCallbackData(Loc(lang, T.Help), "help"), InlineKeyboardButton.WithCallbackData(Loc(lang, T.LanguageButton), "language") }
    });

    private static ReplyKeyboardMarkup MainBarKeyboard(BotLanguage lang) => new(new[]
    {
        new[] { new KeyboardButton(Loc(lang, T.BrowseProducts)) },
        new[] { new KeyboardButton(Loc(lang, T.ViewCart)), new KeyboardButton(Loc(lang, T.MyOrders)) },
        new[] { new KeyboardButton(Loc(lang, T.Help)), new KeyboardButton(Loc(lang, T.LanguageButton)) }
    })
    {
        ResizeKeyboard = true,
        IsPersistent = true
    };

    private static InlineKeyboardMarkup LanguageKeyboard() => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData(BotLocalization.Get(BotLanguage.English, T.LanguageName), "lang:en") },
        new[] { InlineKeyboardButton.WithCallbackData(BotLocalization.Get(BotLanguage.Russian, T.LanguageName), "lang:ru") },
        new[] { InlineKeyboardButton.WithCallbackData(BotLocalization.Get(BotLanguage.Persian, T.LanguageName), "lang:fa") }
    });

    private static InlineKeyboardMarkup BackHomeKeyboard(BotLanguage lang, string backTarget) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData(Loc(lang, T.Back), backTarget), InlineKeyboardButton.WithCallbackData(Loc(lang, T.Home), "home") }
    });

    private static Dictionary<string, string> BuildBarActions()
    {
        var map = new Dictionary<string, string>();
        foreach (var lang in Enum.GetValues<BotLanguage>())
        {
            map[Loc(lang, T.BrowseProducts)] = "products";
            map[Loc(lang, T.ViewCart)] = "cart";
            map[Loc(lang, T.MyOrders)] = "orders";
            map[Loc(lang, T.Help)] = "help";
            map[Loc(lang, T.LanguageButton)] = "language";
        }
        return map;
    }

    // --- render helpers ---

    // The one place inline menus are shown. With a messageId (a button tap) it edits
    // that message in place; without one (a bottom-bar tap) it moves the single live
    // menu to the bottom. Never more than one menu message.
    private async Task RenderAsync(ITelegramBotClient bot, long chatId, int? messageId, string text, InlineKeyboardMarkup keyboard, CancellationToken ct)
    {
        if (messageId is int id && await TryEditAsync(bot, chatId, id, text, keyboard, ct))
        {
            _lastMenu[chatId] = id;
            return;
        }

        if (_lastMenu.TryRemove(chatId, out var oldId) && oldId != messageId)
        {
            await DeleteMessageAsync(bot, chatId, oldId, ct);
        }

        var msg = await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
        _lastMenu[chatId] = msg.MessageId;
    }

    private async Task<bool> TryEditAsync(ITelegramBotClient bot, long chatId, int messageId, string text, InlineKeyboardMarkup? keyboard, CancellationToken ct)
    {
        try
        {
            await bot.EditMessageText(chatId, messageId, text, parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
            return true;
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified"))
        {
            // Content is already correct - treat as success so nothing new is sent.
            return true;
        }
        catch (ApiRequestException)
        {
            // Message can't be edited (too old / deleted) - caller falls back to send.
            return false;
        }
    }

    private async Task DeleteMessageAsync(ITelegramBotClient bot, long chatId, int messageId, CancellationToken ct)
    {
        try
        {
            await bot.DeleteMessage(chatId, messageId, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not delete message {MessageId}", messageId);
        }
    }

    private static string Loc(BotLanguage lang, T key) => BotLocalization.Get(lang, key);
    private static string Loc(BotLanguage lang, T key, params object[] args) => BotLocalization.Get(lang, key, args);

    private static string Money(decimal amount) => amount.ToString("#,0", CultureInfo.InvariantCulture);
}
