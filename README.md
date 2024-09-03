# NymGuardBot

NymGuardBot is a Telegram bot designed to verify that new users joining a chat are not bots by administering a short quiz. The bot will ask a series of questions related to the NYM project, and users need to select the correct answers. The bot will automatically delete its messages after a certain period, ensuring the chat remains clean.

## Features

- **Welcome Message:** The bot sends a personalized welcome message to new users when they join the chat.
- **Quiz for Verification:** New users must answer a series of questions to verify they are not bots.
- **Random Questions:** The bot selects random questions from a predefined set.
- **Timer:** Users have 30 seconds to choose the correct answer. If they fail to do so within the time limit, the bot will record a failed attempt.
- **Incorrect Answers:** If the user selects an incorrect answer, the bot provides feedback and asks another question.
- **Correct Answers:** When the user answers correctly, the bot congratulates them and wishes them well in the NYM community.
- **Automatic Message Deletion:** All messages sent by the bot, including questions and feedback, are automatically deleted after 10 seconds following a correct answer.
- **Ban after Three Failed Attempts:** Users who fail to answer correctly after three attempts are automatically banned from the chat.

## Setup and Installation

1. **Clone the Repository:**

   ```bash
   git clone https://github.com/your-username/NymGuardBot.git
   cd NymGuardBot
