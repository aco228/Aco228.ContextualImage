// See https://aka.ms/new-console-template for more information

using System.Text;
using System.Text.Json;
using Aco228.Common;
using Aco228.Common.Infrastructure;
using Aco228.ContextualImage;
using Aco228.ContextualImage.Consoler;
using Aco228.Common.Extensions;
using Aco228.Common.LocalStorage;
using Aco228.ContextualImage.Infrastructure;
using Aco228.ContextualImage.Services;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;
AcoCommonConfigurable.ProjectName = "CK";
AcoCommonConfigurable.DocumentFolderName = "CKArbo";
AcoCommonConfigurable.TempFolderName = "_temp";

var fontsFolder = StorageManager.Instance.GetFolder("Fonts");
FontManager.LoadFonts(fontsFolder);

var provider = ServiceProviderHelper.CreateProvider(typeof(Program), (builder) =>
{
    builder.RegisterContextualImageServices();
});

var textsRaw = await File.ReadAllTextAsync(@"C:\Users\Lenovo\Desktop\arb.db\contextual-image\texts.json");
var txts = JsonSerializer.Deserialize<List<FbTxt>>(textsRaw).Shuffle().ToManagedList();

var baseFolder = new DirectoryInfo(@"C:\Users\Lenovo\Documents\ArbitrageSoulsStorage\Assets.aco228");
var folders = baseFolder.GetDirectories().ToManagedList();

folders.ShuffleAgain();
int index = 1;

var services = new OrderedList<ContextualFlow>()
{
    // new FlowPrimaryAndSecondaryService(),
    new ContextualImageFlowPrimaryService(),
    // new FlowPrimaryTextBlurService(),
};

for (;;)
{
    var folder = folders.Take();
    var folderInfo = new StorageFolder(folder!);
    var buckets = folderInfo.GetFolder("Buckets");
    var superset = buckets.GetFolder("superset");
    var image = superset.GetDirectoryInfo().GetFiles("*.jpg").Shuffle().FirstOrDefault();
    if (image == null)
        continue;
    
    var txt = txts.Take();
    var service = services.Take();
    await service.Run(
        image.FullName, 
        txt.PrimaryText, 
        txt.SecondaryText, 
        "4:5", 
        width: 1080, 
        height: 1350,
        debug: true);

}



Console.WriteLine("Hello, World!");