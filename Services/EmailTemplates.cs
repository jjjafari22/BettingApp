namespace BettingApp.Services;

public static class EmailTemplates
{
    public const string AccountApprovedTemplate = """
    <!DOCTYPE html>
    <html>
    <head>
        <meta charset="UTF-8">
        <meta name="viewport" content="width=device-width, initial-scale=1.0">
        <title>Account Approved</title>
        <style>
            body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f7fa; margin: 0; padding: 0; -webkit-font-smoothing: antialiased; }
            .email-wrapper { width: 100%; background-color: #f4f7fa; padding: 40px 20px; box-sizing: border-box; }
            .email-content { max-width: 600px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; box-shadow: 0 4px 10px rgba(0, 0, 0, 0.05); overflow: hidden; }
            .header { background-color: #2c3e50; padding: 35px 20px; text-align: center; color: #ffffff; }
            .header h1 { margin: 0; font-size: 26px; font-weight: 600; letter-spacing: 0.5px; }
            .body-content { padding: 35px 40px; color: #333333; line-height: 1.6; font-size: 16px; }
            .greeting { font-size: 20px; font-weight: 600; margin-bottom: 20px; color: #2c3e50; }
            .button-container { text-align: center; margin: 35px 0; }
            .btn-discord { background-color: #5865F2; color: #ffffff !important; text-decoration: none; padding: 14px 28px; border-radius: 6px; font-weight: bold; display: inline-block; font-size: 16px; box-shadow: 0 4px 6px rgba(88, 101, 242, 0.3); }
            .secondary-contact { background-color: #f9f9f9; border-left: 4px solid #0084ff; padding: 15px 20px; margin-top: 30px; border-radius: 0 4px 4px 0; font-size: 15px; }
            .secondary-contact a { color: #0084ff; font-weight: 600; text-decoration: none; }
            .secondary-contact a:hover { text-decoration: underline; }
            .footer { text-align: center; padding: 25px; font-size: 14px; color: #888888; border-top: 1px solid #eeeeee; background-color: #fafafa; }
            @media only screen and (max-width: 600px) { .body-content { padding: 25px 20px; } }
        </style>
    </head>
    <body>
        <div class="email-wrapper">
            <div class="email-content">
                <div class="header">
                    <h1>Welcome to The Castle of Happiness!</h1>
                </div>
                <div class="body-content">
                    <div class="greeting">Congratulations, your account has been approved!</div>
                    <p>We are thrilled to let you know that your registration has been successfully reviewed and approved by our admin team.</p>
                    <p>To get started, meet the community, and receive more information from the admins, please join our official Discord server below:</p>
                    <div class="button-container">
                        <a href="https://discord.gg/rNnsQWENMs" class="btn-discord">Join Our Discord Server</a>
                    </div>
                    <div class="secondary-contact">
                        <strong>Having trouble with Discord?</strong><br>
                        No problem! You can also reach out to our admin team directly via <a href="https://m.me/61587418012797" target="_blank">Facebook Messenger</a>.
                    </div>
                    <p style="margin-top: 35px; font-weight: 500;">We wish you the absolute best of luck!</p>
                    <p style="margin: 0; color: #666;">— The Admins</p>
                </div>
                <div class="footer">
                    &copy; 2026 The Castle of Happiness. All rights reserved.
                </div>
            </div>
        </div>
    </body>
    </html>
    """;
}