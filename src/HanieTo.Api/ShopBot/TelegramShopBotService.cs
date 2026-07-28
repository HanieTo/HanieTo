using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.ShopBot;

// Interactive Telegram bot for browsing the product catalog and placing orders -
// a different concern from Publishing/Publishers/TelegramPublisher.cs, which only
// pushes one-way announcements to a channel. This one has a back-and-forth
// conversation with individual customers.
//
// Uses long-polling (getUpdates) rather than a webhook, so it works immediately
// on a local machine without needing ngrok or any public URL.
//
// UX notes:
// - Callback navigation edits the existing message in place (editMessageText)
//   instead of sending new ones, so the chat doesn't fill up with stacked menus.
// - Every screen carries consistent Back / Home buttons.
// - Trilingual: each chat's chosen language is persisted in ChatPreferences and
//   every string comes from BotLocalization.
public class TelegramShopBotService(
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramShopBotService> logger) : BackgroundService
{
    private long _offset;
    private string _baseUrl = "";

    // The single "live" menu message per chat. Keeping one message and editing it
    // (rather than sending a new one each time) is what stops the chat from filling
    // with stacked menus. In-memory is fine: if the bot restarts, at worst the next
    // interaction spins up one fresh menu.
    private readonly ConcurrentDictionary<long, long> _lastMenu = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botToken = configuration["ShopBot:TelegramBotToken"];
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("ShopBot:TelegramBotToken is not configured - shop bot is disabled.");
            return;
        }

        _baseUrl = $"https://api.telegram.org/bot{botToken}";
        var client = httpClientFactory.CreateClient();
        logger.LogInformation("Shop bot started, polling for updates.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var update in await GetUpdatesAsync(client, stoppingToken))
                {
                    await HandleUpdateAsync(client, update, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error while polling shop bot updates");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<List<JsonElement>> GetUpdatesAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync($"{_baseUrl}/getUpdates?offset={_offset}&timeout=25", ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var json = JsonDocument.Parse(body);

        var updates = new List<JsonElement>();
        if (!json.RootElement.TryGetProperty("result", out var result))
        {
            return updates;
        }

        foreach (var update in result.EnumerateArray())
        {
            updates.Add(update.Clone());
            _offset = update.GetProperty("update_id").GetInt64() + 1;
        }

        return updates;
    }

    private async Task HandleUpdateAsync(HttpClient client, JsonElement update, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A typed message (e.g. /start, or the user just typing something). Rather
        // than spawning a new menu every time, delete what they typed and refresh
        // the single existing menu in place - so the chat stays to one clean menu.
        if (update.TryGetProperty("message", out var message))
        {
            var chatId = message.GetProperty("chat").GetProperty("id").GetInt64();
            var userMessageId = message.GetProperty("message_id").GetInt64();
            await DeleteMessageAsync(client, chatId, userMessageId, ct);

            var pref = await db.ChatPreferences.FirstOrDefaultAsync(p => p.ChatId == chatId.ToString(), ct);
            var (text, keyboard) = pref is null
                ? (BotLocalization.Get(BotLanguage.English, T.ChooseLanguage), LanguageKeyboard())
                : (Loc(pref.Language, T.WelcomeMenu), MainMenuKeyboard(pref.Language));

            await RenderMenuAsync(client, chatId, text, keyboard, ct);
            return;
        }

        // A button tap - edit the existing message in place.
        if (update.TryGetProperty("callback_query", out var callback))
        {
            var callbackId = callback.GetProperty("id").GetString()!;
            var msg = callback.GetProperty("message");
            var chatId = msg.GetProperty("chat").GetProperty("id").GetInt64();
            var messageId = msg.GetProperty("message_id").GetInt64();
            var data = callback.TryGetProperty("data", out var dataProp) ? dataProp.GetString() ?? "" : "";

            // This tapped message becomes the live menu we keep editing.
            _lastMenu[chatId] = messageId;

            await AnswerCallbackAsync(client, callbackId, ct);
            await RouteAsync(client, db, chatId, messageId, data, ct);
        }
    }

    private async Task RouteAsync(HttpClient client, AppDbContext db, long chatId, long messageId, string data, CancellationToken ct)
    {
        var parts = data.Split(':');
        var action = parts.ElementAtOrDefault(0);

        // Language selection is available before a preference exists.
        if (action == "lang")
        {
            await SetLanguageAsync(client, db, chatId, messageId, parts.ElementAtOrDefault(1) ?? "en", ct);
            return;
        }

        var lang = await GetLanguageAsync(db, chatId, ct);

        switch (action)
        {
            case "home":
                await EditAsync(client, chatId, messageId, Loc(lang, T.WelcomeMenu), MainMenuKeyboard(lang), ct);
                break;
            case "language":
                await EditAsync(client, chatId, messageId, Loc(lang, T.ChooseLanguage), LanguageKeyboard(), ct);
                break;
            case "products":
                await ShowCategoriesAsync(client, db, chatId, messageId, lang, ct);
                break;
            case "cat":
                await ShowProductsAsync(client, db, chatId, messageId, lang, parts.ElementAtOrDefault(1) ?? "", ct);
                break;
            case "prod":
                await ShowProductAsync(client, db, chatId, messageId, lang, Guid.Parse(parts[1]), ct);
                break;
            case "qty":
                await ShowQuantityAsync(client, db, chatId, messageId, lang, Guid.Parse(parts[1]), ct);
                break;
            case "addcart":
                await AddToCartAsync(client, db, chatId, messageId, lang, Guid.Parse(parts[1]), int.Parse(parts[2]), ct);
                break;
            case "cart":
                await ShowCartAsync(client, db, chatId, messageId, lang, ct);
                break;
            case "checkout":
                await CheckoutAsync(client, db, chatId, messageId, lang, ct);
                break;
            case "clearcart":
                await ClearCartAsync(client, db, chatId, messageId, lang, ct);
                break;
            case "orders":
                await ShowOrdersAsync(client, db, chatId, messageId, lang, ct);
                break;
            case "help":
                await EditAsync(client, chatId, messageId, Loc(lang, T.HelpText), BackHomeKeyboard(lang, "home"), ct);
                break;
            default:
                await EditAsync(client, chatId, messageId, Loc(lang, T.WelcomeMenu), MainMenuKeyboard(lang), ct);
                break;
        }
    }

    private async Task SetLanguageAsync(HttpClient client, AppDbContext db, long chatId, long messageId, string code, CancellationToken ct)
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

        await EditAsync(client, chatId, messageId, Loc(lang, T.WelcomeMenu), MainMenuKeyboard(lang), ct);
    }

    private async Task<BotLanguage> GetLanguageAsync(AppDbContext db, long chatId, CancellationToken ct)
    {
        var pref = await db.ChatPreferences.FirstOrDefaultAsync(p => p.ChatId == chatId.ToString(), ct);
        return pref?.Language ?? BotLanguage.English;
    }

    private async Task ShowCategoriesAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, CancellationToken ct)
    {
        var categories = await db.Products
            .Where(p => p.IsActive && p.Category != null)
            .Select(p => p.Category!)
            .Distinct()
            .ToListAsync(ct);

        if (categories.Count == 0)
        {
            await ShowProductsAsync(client, db, chatId, messageId, lang, "", ct);
            return;
        }

        var rows = categories.Select(c => new[] { Button($"📁 {c}", $"cat:{c}") }).ToList();
        rows.Add([Button(Loc(lang, T.Home), "home")]);

        await EditAsync(client, chatId, messageId, Loc(lang, T.Categories), Keyboard(rows), ct);
    }

    private async Task ShowProductsAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, string category, CancellationToken ct)
    {
        var query = db.Products.Where(p => p.IsActive);
        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(p => p.Category == category);
        }
        var products = await query.ToListAsync(ct);

        if (products.Count == 0)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.NoProducts), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var rows = products.Select(p =>
        {
            var label = p.Stock > 0
                ? $"{p.Name} — {Money(p.Price)}"
                : $"{p.Name} — {Loc(lang, T.OutOfStockShort)}";
            return new[] { Button(label, $"prod:{p.Id:N}") };
        }).ToList();
        rows.Add([Button(Loc(lang, T.Back), "products"), Button(Loc(lang, T.Home), "home")]);

        var title = string.IsNullOrEmpty(category)
            ? Loc(lang, T.PickProduct, "🛍")
            : Loc(lang, T.PickProduct, category);
        await EditAsync(client, chatId, messageId, title, Keyboard(rows), ct);
    }

    private async Task ShowProductAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.ProductGone), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var text =
            $"🛍 *{product.Name}*\n\n" +
            (string.IsNullOrWhiteSpace(product.Description) ? "" : $"{product.Description}\n\n") +
            $"💰 {Loc(lang, T.ProductPrice)}: *{Money(product.Price)}*\n" +
            $"📦 {Loc(lang, T.ProductStock)}: {product.Stock}";

        var backTarget = product.Category is not null ? $"cat:{product.Category}" : "products";
        var rows = new List<object[]>();
        if (product.Stock > 0)
        {
            rows.Add([Button(Loc(lang, T.BuyButton), $"qty:{product.Id:N}")]);
        }
        rows.Add([Button(Loc(lang, T.Back), backTarget), Button(Loc(lang, T.Home), "home")]);

        await EditAsync(client, chatId, messageId, text, Keyboard(rows), ct);
    }

    private async Task ShowQuantityAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null || product.Stock <= 0)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.OutOfStock), BackHomeKeyboard(lang, "products"), ct);
            return;
        }

        var max = Math.Min(product.Stock, 5);
        var qtyButtons = Enumerable.Range(1, max)
            .Select(n => Button(n.ToString(), $"addcart:{product.Id:N}:{n}"))
            .ToArray();

        List<object[]> rows =
        [
            qtyButtons,
            [Button(Loc(lang, T.Back), $"prod:{product.Id:N}"), Button(Loc(lang, T.Home), "home")]
        ];

        await EditAsync(client, chatId, messageId, $"🛍 *{product.Name}*\n\n{Loc(lang, T.ChooseQuantity)}", Keyboard(rows), ct);
    }

    private async Task AddToCartAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, Guid productId, int quantity, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([productId], ct);
        if (product is null || product.Stock < quantity)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.OutOfStock), BackHomeKeyboard(lang, "products"), ct);
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

        List<object[]> rows =
        [
            [Button(Loc(lang, T.ViewCart), "cart")],
            [Button(Loc(lang, T.ContinueShopping), "products"), Button(Loc(lang, T.Home), "home")]
        ];
        await EditAsync(client, chatId, messageId, $"{Loc(lang, T.AddedToCart)}\n\n🛍 {product.Name} × {quantity}", Keyboard(rows), ct);
    }

    private async Task ShowCartAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, CancellationToken ct)
    {
        var (lines, total, empty) = await BuildCartAsync(db, chatId, ct);
        if (empty)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.CartEmpty), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        var text = $"{Loc(lang, T.CartTitle)}\n\n{lines}\n\n*{Loc(lang, T.OrderTotal)}: {Money(total)}*";
        List<object[]> rows =
        [
            [Button(Loc(lang, T.Checkout), "checkout")],
            [Button(Loc(lang, T.ClearCart), "clearcart"), Button(Loc(lang, T.ContinueShopping), "products")],
            [Button(Loc(lang, T.Home), "home")]
        ];
        await EditAsync(client, chatId, messageId, text, Keyboard(rows), ct);
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

    private async Task CheckoutAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, CancellationToken ct)
    {
        var cart = await db.CartItems.Where(c => c.ChatId == chatId.ToString()).ToListAsync(ct);
        if (cart.Count == 0)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.CartEmpty), BackHomeKeyboard(lang, "home"), ct);
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
            // Cap at available stock in case it dropped since the item was added.
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
            await EditAsync(client, chatId, messageId, Loc(lang, T.OutOfStock), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        db.Orders.Add(order);
        db.CartItems.RemoveRange(cart);
        await db.SaveChangesAsync(ct);

        var summary = string.Join("\n", order.Items.Select(i => $"• {i.ProductName} × {i.Quantity} — {Money(i.UnitPrice * i.Quantity)}"));
        var text = Loc(lang, T.OrderPlaced, summary, Loc(lang, T.OrderTotal), Money(order.Total), Loc(lang, T.OrderId), order.Id.ToString("N"));
        await EditAsync(client, chatId, messageId, text, BackHomeKeyboard(lang, "home"), ct);

        await NotifyAdminAsync(client, chatId, order, ct);
    }

    private async Task ClearCartAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, CancellationToken ct)
    {
        var cart = await db.CartItems.Where(c => c.ChatId == chatId.ToString()).ToListAsync(ct);
        db.CartItems.RemoveRange(cart);
        await db.SaveChangesAsync(ct);
        await EditAsync(client, chatId, messageId, Loc(lang, T.CartEmpty), BackHomeKeyboard(lang, "home"), ct);
    }

    // Notifies the shop owner (if ShopBot:AdminChatId is configured) when an order
    // is placed - a pattern taken from production shop bots like AiogramShopBot.
    private async Task NotifyAdminAsync(HttpClient client, long buyerChatId, Order order, CancellationToken ct)
    {
        var adminChatId = configuration["ShopBot:AdminChatId"];
        if (string.IsNullOrWhiteSpace(adminChatId))
        {
            return;
        }

        var summary = string.Join("\n", order.Items.Select(i => $"• {i.ProductName} × {i.Quantity} — {Money(i.UnitPrice * i.Quantity)}"));
        var text = $"🔔 *New order*\nFrom chat: `{buyerChatId}`\n\n{summary}\n\n*Total: {Money(order.Total)}*\nOrder ID: `{order.Id:N}`";
        try
        {
            await SendAsync(client, long.Parse(adminChatId), text, Keyboard([]), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin order notification");
        }
    }

    private async Task ShowOrdersAsync(HttpClient client, AppDbContext db, long chatId, long messageId, BotLanguage lang, CancellationToken ct)
    {
        var orders = await db.Orders
            .Include(o => o.Items)
            .Where(o => o.BuyerChatId == chatId.ToString())
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(10)
            .ToListAsync(ct);

        if (orders.Count == 0)
        {
            await EditAsync(client, chatId, messageId, Loc(lang, T.NoOrders), BackHomeKeyboard(lang, "home"), ct);
            return;
        }

        var text = Loc(lang, T.YourOrders) + "\n\n" + string.Join("\n\n", orders.Select(o =>
            $"🗓 {o.CreatedAtUtc:yyyy-MM-dd HH:mm}\n" +
            string.Join("\n", o.Items.Select(i => $"• {i.ProductName} × {i.Quantity} — {Money(i.UnitPrice * i.Quantity)}")) +
            $"\n*{Loc(lang, T.OrderTotal)}: {Money(o.Total)}*"));

        await EditAsync(client, chatId, messageId, text, BackHomeKeyboard(lang, "home"), ct);
    }

    // --- keyboards ---

    private static object MainMenuKeyboard(BotLanguage lang) => Keyboard(
    [
        [Button(Loc(lang, T.BrowseProducts), "products")],
        [Button(Loc(lang, T.ViewCart), "cart"), Button(Loc(lang, T.MyOrders), "orders")],
        [Button(Loc(lang, T.Help), "help"), Button(Loc(lang, T.LanguageButton), "language")]
    ]);

    private static object LanguageKeyboard() => Keyboard(
    [
        [Button(BotLocalization.Get(BotLanguage.English, T.LanguageName), "lang:en")],
        [Button(BotLocalization.Get(BotLanguage.Russian, T.LanguageName), "lang:ru")],
        [Button(BotLocalization.Get(BotLanguage.Persian, T.LanguageName), "lang:fa")]
    ]);

    private static object BackHomeKeyboard(BotLanguage lang, string backTarget) => Keyboard(
    [
        [Button(Loc(lang, T.Back), backTarget), Button(Loc(lang, T.Home), "home")]
    ]);

    private static object Keyboard(IEnumerable<object[]> rows) => new { inline_keyboard = rows };

    private static object Button(string text, string callbackData) => new { text, callback_data = callbackData };

    // --- Telegram calls ---

    // Shows content in the chat's single live menu message: edits it if we have one,
    // otherwise sends a new message and remembers its id.
    private async Task RenderMenuAsync(HttpClient client, long chatId, string text, object keyboard, CancellationToken ct)
    {
        if (_lastMenu.TryGetValue(chatId, out var existingId) &&
            await TryEditAsync(client, chatId, existingId, text, keyboard, ct))
        {
            return;
        }

        var newId = await SendAsync(client, chatId, text, keyboard, ct);
        if (newId.HasValue)
        {
            _lastMenu[chatId] = newId.Value;
        }
    }

    private async Task<long?> SendAsync(HttpClient client, long chatId, string text, object keyboard, CancellationToken ct)
    {
        var body = new { chat_id = chatId, text, parse_mode = "Markdown", reply_markup = keyboard };
        using var content = JsonContent.Create(body);
        var response = await client.PostAsync($"{_baseUrl}/sendMessage", content, ct);

        try
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(responseBody);
            if (json.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("message_id", out var mid))
            {
                return mid.GetInt64();
            }
        }
        catch (JsonException) { /* ignore - just means we won't track this id */ }

        return null;
    }

    private async Task EditAsync(HttpClient client, long chatId, long messageId, string text, object keyboard, CancellationToken ct)
    {
        if (await TryEditAsync(client, chatId, messageId, text, keyboard, ct))
        {
            _lastMenu[chatId] = messageId;
            return;
        }

        // Message can't be edited (too old, or already identical) - send a fresh one.
        var newId = await SendAsync(client, chatId, text, keyboard, ct);
        if (newId.HasValue)
        {
            _lastMenu[chatId] = newId.Value;
        }
    }

    private async Task<bool> TryEditAsync(HttpClient client, long chatId, long messageId, string text, object keyboard, CancellationToken ct)
    {
        var body = new { chat_id = chatId, message_id = messageId, text, parse_mode = "Markdown", reply_markup = keyboard };
        using var content = JsonContent.Create(body);
        var response = await client.PostAsync($"{_baseUrl}/editMessageText", content, ct);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        // "message is not modified" means the tapped button renders exactly what's
        // already showing (e.g. Home while already Home). The menu is already correct,
        // so treat it as success - sending a new message here is what caused menus to
        // stack up.
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        return responseBody.Contains("message is not modified");
    }

    // Best-effort deletion of a user's typed message so the chat stays tidy. Bots
    // can delete messages in a private chat within 48h; failures are ignored.
    private async Task DeleteMessageAsync(HttpClient client, long chatId, long messageId, CancellationToken ct)
    {
        try
        {
            using var content = JsonContent.Create(new { chat_id = chatId, message_id = messageId });
            await client.PostAsync($"{_baseUrl}/deleteMessage", content, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not delete user message {MessageId}", messageId);
        }
    }

    private async Task AnswerCallbackAsync(HttpClient client, string callbackQueryId, CancellationToken ct)
    {
        using var content = JsonContent.Create(new { callback_query_id = callbackQueryId });
        await client.PostAsync($"{_baseUrl}/answerCallbackQuery", content, ct);
    }

    private static string Loc(BotLanguage lang, T key) => BotLocalization.Get(lang, key);
    private static string Loc(BotLanguage lang, T key, params object[] args) => BotLocalization.Get(lang, key, args);

    // Neutral thousands-grouped number, no currency symbol - the shop's prices are
    // plain amounts and a culture-based "$" would be misleading.
    private static string Money(decimal amount) => amount.ToString("#,0", CultureInfo.InvariantCulture);
}
