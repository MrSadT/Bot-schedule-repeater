using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using System.Runtime.InteropServices;
using Telegram.Bot;
using TL;
using WTelegram;

internal class Schedule
{
    const int UpdateTimer = 60; //Time in minutes until next update info.
    const int visibleConsole = 0; //0 - hide; 1 - show.
    //Site
    const string Address = "http://_institution_/ua/schedule"; // A link to the site we'll be parsing.
    const string SiteSelector = "div.schedule__den div.card"; // Selector, which points to schedule cells.

    //Telegram
    const long ChatId = -1002482381801; //telegram.bot API id of chat. You can get this from reference of the chat.
    const long TlChatId = 2482381801; //WTelegram API id of chat. You NOT can get this from reference of the chat.

    const string TgToken = "TOKEN"; //The token of your telegram bot.
    const string api_id = "api_id"; //You can get this there: https://my.telegram.org/apps
    const string api_hash = "api_hash"; //You can get this there: https://my.telegram.org/apps


    /// <summary>
    /// Where the phone number is stored
    /// </summary>
    static string filePath = Path.Combine(Environment.CurrentDirectory, "LocalData.txt");
    
    /// <summary>
    /// Saves and returns phone numer from .txt file in project directory
    /// </summary>
    static string PhoneNumber //there validation works for ukrainian numbers!!!
    {
        get 
        { 
            if (File.Exists(Path.Combine(Environment.CurrentDirectory, "LocalData.txt")))
                return File.ReadAllText(filePath);
            return null;
        }
        set //this validation works for ukrainian numbers.
        { 
            if(value.StartsWith("+380") && value.Length == 13)File.WriteAllText(filePath, value);
            if (value.Length == 9) File.WriteAllText(filePath, $"+380{value}");
            else throw new Exception("Wrong number!");
        }
    }

    /// <summary>
    /// Іs used to compare values when sorting.
    /// </summary>
    enum UkrainianMouths
    {
        Січня,
        Лютого,
        Березня,
        Квітня,
        Травня,
        Червня,
        Липня,
        Серпня,
        Вересня,
        Жовтня,
        Листопада,
        Грудня
    } //Change names of elements, if they are different from your.

    static Client Client;
    static User Myself;//Uses for log in
    static TelegramBotClient BotClient;

    static IntPtr Window;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static async Task Main(string[] _)
    {
        Window = GetConsoleWindow();

        await InitTelegram();

        ShowWindow(Window, visibleConsole);

        while (true)
        {
            var dataTelegram = await GetTelegramMessagesAsync();
            var dataSite = await ParseSiteAsync();

            var dataToPost = ContainsInTelegram(dataTelegram, dataSite);
            Post(SortShedule(dataToPost));

            Thread.Sleep(UpdateTimer * 60*1000);
        }
    }

    /// <summary>
    /// Initialization of variable and log in client WTelegram.
    /// </summary>
    /// <returns></returns>
    static async Task InitTelegram()
    {
        Console.WriteLine("\tLog in to your account to view a schedule that has already been sent\nNOTE: The bots themselves can't read chats, so you have to log in as a client to receive messages.");
        static string Config(string what)
        {
            switch (what)
            {
                case "api_id": return "api_id";
                case "api_hash": return "api_hash";
                case "phone_number":if (PhoneNumber == null) { Console.Write("Enter the phone number on which the telegram is registered:\nNOTE: If an invalid number is entered, it can be changed in LocalData.txt ");
                                        PhoneNumber = Console.ReadLine(); } return PhoneNumber;
                case "verification_code": Console.Write("Verefication code: "); return Console.ReadLine();
                case "first_name": Console.Write("First name: "); return Console.ReadLine();      // if sign-up is required
                case "last_name": Console.Write("Last name: "); return Console.ReadLine();        // if sign-up is required
                case "password": Console.Write("2 factor autentification code: "); return Console.ReadLine();     // if user has enabled 2FA
                default: return null;
            }
        }

        Client = new WTelegram.Client(Config);
        Myself = await Client.LoginUserIfNeeded();
        BotClient = new TelegramBotClient(TgToken);
    }

    /// <summary>
    /// Posts schedule to telegram.
    /// </summary>
    /// <param name="newData"></param>    
    static void Post(List<string> newData)
    {
        if (newData == null || newData.Count == 0) {Console.WriteLine("Nothing to post..."); return; }
        foreach (var data in newData)
        {
            Console.WriteLine($"Posted >>>\t{data}");
            BotClient.SendTextMessageAsync(ChatId, data);
            Thread.Sleep(1000);
        }
    }

    /// <summary>
    /// Parses site
    /// </summary>
    /// <param name="address"></param>
    /// <param name="selector"></param>
    /// <returns>Schedule in format (Data\nReferenceToImage)</returns>
    static async Task<List<string>> ParseSiteAsync() //Maybe you will need change there parser.
    {
        var context = BrowsingContext.New(Configuration.Default.WithDefaultLoader());

        var document = await context.OpenAsync(Address);
        var cells = document.QuerySelectorAll(SiteSelector);

        var cellsData = new List<string>();
        foreach (var cell in cells) //Change this part, if your site have different layout in cells.
        {
            var date = String.Join(" ", cell.TextContent.Split(["\n"," "], StringSplitOptions.RemoveEmptyEntries));
            var image = cell.QuerySelector<IHtmlImageElement>("img")!.Source;

            cellsData.Add(($"{date}\n{image}")); //Fix sort comparator if you changed this.
        }
        return cellsData;
    }

    /// <summary>
    /// Gets messages from telegram chat
    /// </summary>
    /// <returns></returns>
    static async Task<List<string>> GetTelegramMessagesAsync()
    {
        List<string> result = new();

        var chats = await Client.Messages_GetAllChats();
        InputPeer channelPeer = chats.chats[TlChatId];
        for (int offset_id = 0; ;)
        {
            var messages = await Client.Messages_GetHistory(channelPeer, offset_id,limit:30);;
            if (messages.Messages.Length == 0) break;

            foreach (var msgBase in messages.Messages)
            {
                var from = messages.UserOrChat(msgBase.From ?? msgBase.Peer); // from can be User/Chat/Channel
                if (msgBase is Message msg)
                    result.Add(msg.message);
            }
            offset_id = messages.Messages[^1].ID;
        }
        return result;
    }

    /// <summary>
    /// Checks the presence in the telegram and returns the difference
    /// </summary>
    /// <param name="fromTelegram"></param>
    /// <param name="fromSite"></param>
    /// <returns>Missing schedule in the telegram</returns>
    static List<string> ContainsInTelegram(List<string> fromTelegram, List<string> fromSite)
    {
        var absents = new List<string>();

        foreach (var siteShedule in fromSite)
            if (!fromTelegram.Contains(siteShedule))
                absents.Add(siteShedule);
        return absents;
    }

    /// <summary>
    /// Sorts schedule
    /// </summary>
    /// <param name="schedule"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    static List<string> SortShedule(List<string> schedule) //Maybe you will need change comparator, if you have changed a parser of site.
    {
        if (schedule.Count == 0) return null;
        Dictionary<string,string> dictionary = new();
        foreach (var shedule_ in schedule)
            dictionary.Add(shedule_.Split("\n")[0], shedule_);
        var sortedKeys = BubbleSort<string>(dictionary.Keys.ToArray(), (first,second) => //comparator
        {
            var firstValue = first.Split(' ');
            var secondValue = second.Split(' ');

            if (firstValue.Length != secondValue.Length || firstValue.Length != 2) throw new Exception("Іmpossible to sort!!!");

            var firstMounth = (UkrainianMouths)Enum.Parse(typeof(UkrainianMouths), firstValue[1], true);
            var secondMounth = (UkrainianMouths)Enum.Parse(typeof(UkrainianMouths), secondValue[1], true);

            if (firstMounth == secondMounth)
                return int.Parse(firstValue[0]) > int.Parse(secondValue[0]);
            return firstMounth > secondMounth;
        });
        List<string> result = new();
        foreach (var key in sortedKeys)
            result.Add(dictionary[key]);
        return result;
    }

    /// <summary>
    /// Standard bubble sort
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="array"></param>
    /// <param name="Equal"></param>
    /// <returns></returns>
    static T[] BubbleSort<T>(T[] array,Func<T,T,bool> Equal)
    {
        int n = array.Length;
        for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < n - i - 1; j++)
                if (Equal(array[j], array[j + 1]))
                    (array[j + 1], array[j]) = (array[j], array[j + 1]);
        return array;
    }
}
