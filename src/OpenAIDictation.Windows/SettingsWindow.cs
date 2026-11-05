using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenAIDictation.Core;

namespace OpenAIDictation.Windows;

internal sealed class SettingsWindow:Window
{
    public SettingsWindow()
    {
        Title="OpenAI Dictation — Settings";Width=560;Height=550;MinWidth=480;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(Color.FromRgb(30,30,32));Foreground=Brushes.White;
        var root=new StackPanel{Margin=new Thickness(24)};Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        root.Children.Add(new TextBlock{Text="OpenAI Dictation",FontSize=24,FontWeight=FontWeights.SemiBold});
        root.Children.Add(new TextBlock{Text="Win+H starts and stops. Escape cancels. Audio goes directly to OpenAI.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,20)});
        root.Children.Add(new TextBlock{Text="OpenAI API key (leave blank to keep the saved key)"});
        var key=new PasswordBox{Margin=new Thickness(0,8,0,18),Padding=new Thickness(8)};root.Children.Add(key);
        DictationOptions saved=LocalStore.LoadOptions();
        root.Children.Add(new TextBlock{Text="Vocabulary — names and terms, separated by commas or new lines"});
        var vocabulary=new TextBox{Text=saved.Vocabulary,AcceptsReturn=true,Height=85,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,18),Padding=new Thickness(8)};root.Children.Add(vocabulary);
        root.Children.Add(new TextBlock{Text="Additional cleanup instructions"});
        var instructions=new TextBox{Text=saved.Instructions,AcceptsReturn=true,Height=85,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16),Padding=new Thickness(8)};root.Children.Add(instructions);
        var status=new TextBlock{Text=LocalStore.HasKey?"API key saved for this Windows account.":"Save your API key to enable dictation.",TextWrapping=TextWrapping.Wrap};root.Children.Add(status);
        var save=new Button{Content="Save on this PC",Padding=new Thickness(18,8,18,8),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,18,0,0)};root.Children.Add(save);
        save.Click+=(_,_)=>
        {
            try
            {
                if(!string.IsNullOrWhiteSpace(key.Password))LocalStore.SaveKey(key.Password);
                LocalStore.SaveOptions(saved with{Vocabulary=vocabulary.Text,Instructions=instructions.Text});key.Clear();status.Text="Saved. Press Win+H in a text field to dictate.";
            }
            catch(Exception error){status.Text=error is ArgumentException?error.Message:"Could not save these settings.";}
        };
    }
}
