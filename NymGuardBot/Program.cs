using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

class Program
{
    private static ITelegramBotClient botClient = new TelegramBotClient("7202384152:AAEFjafaZe0XWsZ-h-4WmlJa3y-vvaKtxJA");
    private static Dictionary<long, int> userAttempts = new Dictionary<long, int>();
    private static Dictionary<long, string> correctAnswers = new Dictionary<long, string>();
    private static Dictionary<long, Timer> activeTimers = new Dictionary<long, Timer>();
    private static Dictionary<long, List<int>> messageIdsToDelete = new Dictionary<long, List<int>>();
    private static Dictionary<long, string> userLanguages = new Dictionary<long, string>();

    static async Task Main(string[] args)
    {
        Console.WriteLine("Bot is starting...");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>() // Receive all update types
        };

        botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions);

        Console.WriteLine("Bot is running...");
        await Task.Delay(-1);
    }

    private static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var message = update.Message;

            Console.WriteLine($"Received message from {message.From?.Username} ({message.From?.Id}): {message.Text}");

            if (message.Type == MessageType.ChatMembersAdded)
            {
                foreach (var member in message.NewChatMembers)
                {
                    if (!member.IsBot)
                    {
                        await SendLanguageSelectionMessageAsync(message.Chat.Id, member);
                    }
                }
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            Console.WriteLine($"Callback query received from {update.CallbackQuery.From?.Username}");
            await HandleCallbackQueryAsync(update.CallbackQuery);
        }
    }

    private static async Task SendLanguageSelectionMessageAsync(long chatId, User user)
    {
        var languageOptions = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("English", "en"), InlineKeyboardButton.WithCallbackData("Українська", "uk") },
            new[] { InlineKeyboardButton.WithCallbackData("Français", "fr"), InlineKeyboardButton.WithCallbackData("中文", "zh") },
            new[] { InlineKeyboardButton.WithCallbackData("हिन्दी", "hi"), InlineKeyboardButton.WithCallbackData("Español", "es") }
        });

        var welcomeMessage = await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"Hello, @{user.Username}! Please choose your language.\n" +
                  "Оберіть мову.\n" +
                  "Choisissez votre langue.\n" +
                  "请选择您的语言。\n" +
                  "अपनी भाषा चुनें।\n" +
                  "Seleccione su idioma.",
            replyMarkup: languageOptions,
            disableNotification: true
        );

        if (!messageIdsToDelete.ContainsKey(user.Id))
        {
            messageIdsToDelete[user.Id] = new List<int>();
        }

        messageIdsToDelete[user.Id].Add(welcomeMessage.MessageId);
        Console.WriteLine($"Sent language selection message to @{user.Username}");
    }

    private static async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery?.Data == null || callbackQuery.Message == null)
        {
            return;
        }

        long userId = callbackQuery.From.Id;

        if (!userLanguages.ContainsKey(userId))
        {
            userLanguages[userId] = callbackQuery.Data;
            Console.WriteLine($"User {userId} selected language: {callbackQuery.Data}");
            
            if (languageQuestions.ContainsKey(userLanguages[userId]))
            {
                await SendQuestionAsync(callbackQuery.Message.Chat.Id, userId);
            }
            else
            {
                await botClient.SendTextMessageAsync(callbackQuery.Message.Chat.Id, 
                    "Unfortunately, there are no questions available for the selected language.",
                    disableNotification: true);
            }
        }
        else
        {
            if (int.TryParse(callbackQuery.Data, out int userAnswerIndex) && correctAnswers.TryGetValue(userId, out string correctAnswer))
            {
                bool isCorrect = languageQuestions[userLanguages[userId]].Any(q => q.Options[userAnswerIndex] == correctAnswer);

                string feedbackText;
                if (isCorrect)
                {
                    feedbackText = GetWelcomeMessageForPrivacy(userLanguages[userId]);
                    Console.WriteLine($"User {userId} answered correctly");
                }
                else
                {
                    feedbackText = GetFeedbackText(userLanguages[userId], false);
                    Console.WriteLine($"User {userId} answered incorrectly");
                }

                var feedbackMessage = await botClient.SendTextMessageAsync(
                    chatId: callbackQuery.Message.Chat.Id,
                    text: feedbackText,
                    disableNotification: true
                );

                if (!messageIdsToDelete.ContainsKey(userId))
                {
                    messageIdsToDelete[userId] = new List<int>();
                }
                messageIdsToDelete[userId].Add(feedbackMessage.MessageId);

                await Task.Delay(5000);
                await DeleteMessagesAsync(callbackQuery.Message.Chat.Id, userId);
                if (!isCorrect)
                {
                    await SendQuestionAsync(callbackQuery.Message.Chat.Id, userId);
                }
            }
        }
    }

    private static async Task SendQuestionAsync(long chatId, long userId)
    {
        var question = GetRandomQuestion(userLanguages[userId]);
        correctAnswers[userId] = question?.CorrectAnswer ?? "Unknown";

        var inlineKeyboard = new InlineKeyboardMarkup(
            question.Options.Select((option, index) =>
                new[] { InlineKeyboardButton.WithCallbackData(option, index.ToString()) }
            ).ToArray()
        );

        var message = await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: $"{question.Text}\n{GetFeedbackText(userLanguages[userId], true)}",
            replyMarkup: inlineKeyboard,
            disableNotification: true
        );

        if (!messageIdsToDelete.ContainsKey(userId))
        {
            messageIdsToDelete[userId] = new List<int>();
        }
        messageIdsToDelete[userId].Add(message.MessageId);

        var timer = new Timer(async state => await HandleTimeoutAsync(chatId, userId), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        activeTimers[userId] = timer;

        Console.WriteLine($"Sent question to user {userId}");
    }

    private static async Task HandleTimeoutAsync(long chatId, long userId)
    {
        if (activeTimers.ContainsKey(userId))
        {
            activeTimers[userId].Change(Timeout.Infinite, Timeout.Infinite);
            activeTimers.Remove(userId);
        }

        await DeleteMessagesAsync(chatId, userId);

        userAttempts[userId]++;

        if (userAttempts[userId] >= 3)
        {
            await botClient.BanChatMemberAsync(chatId, userId);
            Console.WriteLine($"User {userId} has been banned after 3 failed attempts.");
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "The user has been banned for incorrect answers.",
                disableNotification: true
            );
        }
        else
        {
            await botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "You did not answer in time! This counts as a failed attempt.",
                disableNotification: true
            );
            await Task.Delay(2000);
            await SendQuestionAsync(chatId, userId);
        }
    }

    private static async Task DeleteMessagesAsync(long chatId, long userId)
    {
        if (activeTimers.ContainsKey(userId))
        {
            activeTimers[userId].Change(Timeout.Infinite, Timeout.Infinite);
            activeTimers.Remove(userId);
        }

        if (messageIdsToDelete.ContainsKey(userId) && messageIdsToDelete[userId].Any())
        {
            foreach (var messageId in messageIdsToDelete[userId])
            {
                await botClient.DeleteMessageAsync(chatId, messageId);
            }
            messageIdsToDelete.Remove(userId);
        }

        Console.WriteLine($"Deleted messages for user {userId}");
    }

    // Метод для отримання рандомного питання для обраної мови
    private static Question GetRandomQuestion(string language)
    {
        var random = new Random();
        return languageQuestions[language][random.Next(languageQuestions[language].Count)];
    }

    // Метод для отримання вітального повідомлення для обраної мови
    private static string GetWelcomeMessageForPrivacy(string language)
    {
        switch (language)
        {
            case "en":
                return "Welcome to the Privacy community! We recommend using NYM VPN to protect your metadata and ensure freedom of speech.";
            case "uk":
                return "Вітаємо у спільноті Приватності! Рекомендуємо використовувати NYM VPN для захисту метаданих і забезпечення свободи слова.";
            case "fr":
                return "Bienvenue dans la communauté de la confidentialité! Nous vous recommandons d'utiliser NYM VPN pour protéger vos métadonnées et garantir la liberté d'expression.";
            case "zh":
                return "欢迎加入隐私社区！我们建议使用NYM VPN来保护您的元数据并确保言论自由。";
            case "hi":
                return "गोपनीयता समुदाय में आपका स्वागत है! हम अनुशंसा करते हैं कि आप अपने मेटाडेटा की सुरक्षा के लिए NYM VPN का उपयोग करें।";
            case "es":
                return "¡Bienvenido a la comunidad de Privacidad! Recomendamos utilizar NYM VPN para proteger sus metadatos y garantizar la libertad de expresión.";
            default:
                return "Welcome!";
        }
    }

    // Метод для відображення повідомлення про неправильну відповідь
    private static string GetFeedbackText(string language, bool isQuestion)
    {
        switch (language)
        {
            case "en":
                return isQuestion ? "You have 30 seconds to choose the correct option." : "Incorrect answer! Try again.";
            case "uk":
                return isQuestion ? "У вас є 30 секунд на вибір правильної відповіді." : "Неправильна відповідь! Спробуйте ще раз.";
            case "fr":
                return isQuestion ? "Vous avez 30 secondes pour choisir la bonne réponse." : "Réponse incorrecte! Réessayez.";
            case "zh":
                return isQuestion ? "您有30秒钟选择正确的答案。" : "答案错误！再试一次。";
            case "hi":
                return isQuestion ? "सही उत्तर चुनने के लिए आपके पास 30 सेकंड हैं।" : "उत्तर गलत है! फिर से प्रयास करें।";
            case "es":
                return isQuestion ? "Tienes 30 segundos para elegir la opción correcta." : "¡Respuesta incorrecta! Inténtalo de nuevo.";
            default:
                return "Try again.";
        }
    }

    // Список запитань для кожної мови
    private static readonly Dictionary<string, List<Question>> languageQuestions = new Dictionary<string, List<Question>>
    {
        {
            "en", new List<Question>
            {
                new Question { Text = "What is the capital of France?", Options = new[] { "Berlin", "Madrid", "Paris", "Rome" }, CorrectAnswer = "Paris" },
                new Question { Text = "Which planet is known as the Red Planet?", Options = new[] { "Earth", "Mars", "Jupiter", "Venus" }, CorrectAnswer = "Mars" },
                new Question { Text = "What is the largest ocean on Earth?", Options = new[] { "Atlantic", "Pacific", "Indian", "Arctic" }, CorrectAnswer = "Pacific" },
                new Question { Text = "Which country is home to the kangaroo?", Options = new[] { "USA", "Brazil", "Australia", "China" }, CorrectAnswer = "Australia" },
                new Question { Text = "What is the smallest continent?", Options = new[] { "Asia", "Europe", "Australia", "Antarctica" }, CorrectAnswer = "Australia" },
                new Question { Text = "Which country is known as the land of the rising sun?", Options = new[] { "Japan", "China", "Korea", "Thailand" }, CorrectAnswer = "Japan" },
                new Question { Text = "Who wrote 'Romeo and Juliet'?", Options = new[] { "Shakespeare", "Hemingway", "Tolstoy", "Dante" }, CorrectAnswer = "Shakespeare" }
            }
        },
        {
            "uk", new List<Question>
            {
                new Question { Text = "Яка столиця України?", Options = new[] { "Київ", "Львів", "Одеса", "Харків" }, CorrectAnswer = "Київ" },
                new Question { Text = "Яка найбільша річка в Україні?", Options = new[] { "Дніпро", "Дністер", "Південний Буг", "Тиса" }, CorrectAnswer = "Дніпро" },
                new Question { Text = "Яке місто є культурною столицею України?", Options = new[] { "Харків", "Львів", "Одеса", "Дніпро" }, CorrectAnswer = "Львів" },
                new Question { Text = "Яке найбільше озеро в Україні?", Options = new[] { "Ялпуг", "Світязь", "Синевир", "Куяльник" }, CorrectAnswer = "Ялпуг" },
                new Question { Text = "Яка найвища гора в Україні?", Options = new[] { "Говерла", "Петрос", "Піп Іван", "Ребра" }, CorrectAnswer = "Говерла" },
                new Question { Text = "Який океан найбільший у світі?", Options = new[] { "Атлантичний", "Тихий", "Індійський", "Північний Льодовитий" }, CorrectAnswer = "Тихий" },
                new Question { Text = "Який прапор має синьо-жовті кольори?", Options = new[] { "Польща", "Україна", "Словаччина", "Румунія" }, CorrectAnswer = "Україна" }
            }
        },
        {
            "fr", new List<Question>
            {
                new Question { Text = "Quelle est la capitale de la France?", Options = new[] { "Paris", "Lyon", "Marseille", "Bordeaux" }, CorrectAnswer = "Paris" },
                new Question { Text = "Quelle est la plus grande montagne d'Europe?", Options = new[] { "Mont Blanc", "Mont Everest", "Mont Fuji", "Mont Kilimandjaro" }, CorrectAnswer = "Mont Blanc" },
                new Question { Text = "Quel est le plus grand pays d'Afrique?", Options = new[] { "Égypte", "Algérie", "Nigéria", "Afrique du Sud" }, CorrectAnswer = "Algérie" },
                new Question { Text = "Quelle est la capitale de l'Italie?", Options = new[] { "Rome", "Milan", "Naples", "Florence" }, CorrectAnswer = "Rome" },
                new Question { Text = "Quel pays est connu pour ses pyramides?", Options = new[] { "Grèce", "Égypte", "Inde", "Pérou" }, CorrectAnswer = "Égypte" },
                new Question { Text = "Quel pays est célèbre pour ses fromages?", Options = new[] { "France", "Espagne", "Italie", "Suisse" }, CorrectAnswer = "France" },
                new Question { Text = "Qui a écrit 'Les Misérables'?", Options = new[] { "Victor Hugo", "Molière", "Balzac", "Flaubert" }, CorrectAnswer = "Victor Hugo" }
            }
        },
        {
            "zh", new List<Question>
            {
                new Question { Text = "中国的首都是什么?", Options = new[] { "上海", "北京", "广州", "深圳" }, CorrectAnswer = "北京" },
                new Question { Text = "哪一个是世界上最高的山?", Options = new[] { "珠穆朗玛峰", "乞力马扎罗山", "富士山", "白朗峰" }, CorrectAnswer = "珠穆朗玛峰" },
                new Question { Text = "中国的最大河流是哪条?", Options = new[] { "黄河", "长江", "珠江", "牡丹江" }, CorrectAnswer = "长江" },
                new Question { Text = "中国有多少个省?", Options = new[] { "20", "22", "23", "34" }, CorrectAnswer = "23" },
                new Question { Text = "哪一个国家以金字塔著名?", Options = new[] { "希腊", "埃及", "印度", "祕鲁" }, CorrectAnswer = "埃及" },
                new Question { Text = "中国的传统节日是什么?", Options = new[] { "春节", "中秋节", "端午节", "所有这些" }, CorrectAnswer = "所有这些" },
                new Question { Text = "中国的国宝是什么动物?", Options = new[] { "大熊猫", "老虎", "狮子", "大象" }, CorrectAnswer = "大熊猫" }
            }
        },
        {
            "hi", new List<Question>
            {
                new Question { Text = "भारत की राजधानी क्या है?", Options = new[] { "मुंबई", "दिल्ली", "कोलकाता", "चेन्नई" }, CorrectAnswer = "दिल्ली" },
                new Question { Text = "भारत की सबसे बड़ी नदी कौन सी है?", Options = new[] { "गंगा", "यमुना", "गोदावरी", "कृष्णा" }, CorrectAnswer = "गंगा" },
                new Question { Text = "भारत का सबसे बड़ा राज्य कौन सा है?", Options = new[] { "उत्तर प्रदेश", "मध्य प्रदेश", "राजस्थान", "बिहार" }, CorrectAnswer = "राजस्थान" },
                new Question { Text = "ताजमहल कहाँ स्थित है?", Options = new[] { "आगरा", "दिल्ली", "जयपुर", "लखनऊ" }, CorrectAnswer = "आगरा" },
                new Question { Text = "भारत में कितने राज्य हैं?", Options = new[] { "25", "28", "29", "30" }, CorrectAnswer = "28" },
                new Question { Text = "भारत का राष्ट्रीय पशु क्या है?", Options = new[] { "शेर", "चीता", "बाघ", "हाथी" }, CorrectAnswer = "बाघ" },
                new Question { Text = "भारत का राष्ट्रीय खेल क्या है?", Options = new[] { "क्रिकेट", "कबड्डी", "हॉकी", "फुटबॉल" }, CorrectAnswer = "हॉकी" }
            }
        },
        {
            "es", new List<Question>
            {
                new Question { Text = "¿Cuál es la capital de España?", Options = new[] { "Madrid", "Barcelona", "Sevilla", "Valencia" }, CorrectAnswer = "Madrid" },
                new Question { Text = "¿Qué océano está al oeste de América del Sur?", Options = new[] { "Atlántico", "Pacífico", "Índico", "Ártico" }, CorrectAnswer = "Pacífico" },
                new Question { Text = "¿Quién pintó la Mona Lisa?", Options = new[] { "Picasso", "Da Vinci", "Van Gogh", "Rembrandt" }, CorrectAnswer = "Da Vinci" },
                new Question { Text = "¿Cuál es el país más grande de América Latina?", Options = new[] { "México", "Brasil", "Argentina", "Chile" }, CorrectAnswer = "Brasil" },
                new Question { Text = "¿Cuál es el idioma oficial de Brasil?", Options = new[] { "Español", "Portugués", "Inglés", "Francés" }, CorrectAnswer = "Portugués" },
                new Question { Text = "¿Quién escribió 'Cien años de soledad'?", Options = new[] { "Gabriel García Márquez", "Pablo Neruda", "Jorge Luis Borges", "Octavio Paz" }, CorrectAnswer = "Gabriel García Márquez" },
                new Question { Text = "¿Cuál es el río más largo del mundo?", Options = new[] { "Nilo", "Amazonas", "Yangtsé", "Misisipi" }, CorrectAnswer = "Amazonas" }
            }
        }
    };

    private class Question
    {
        public string Text { get; set; } = string.Empty;
        public string[] Options { get; set; } = Array.Empty<string>();
        public string CorrectAnswer { get; set; } = string.Empty;
    }

    private static Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine($"Error: {errorMessage}");
        return Task.CompletedTask;
    }
}