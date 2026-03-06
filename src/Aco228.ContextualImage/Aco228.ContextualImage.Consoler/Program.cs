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

var folders = new ManagedList<string>()
{
    "C:\\Users\\Lenovo\\Documents\\ArbitrageSoulsStorage\\Assets.aco228\\UsedCars\\Buckets\\superset",
    "C:\\Users\\Lenovo\\Documents\\ArbitrageSoulsStorage\\Assets.aco228\\UsedAudiSUV\\Buckets\\superset"
};

for (;;)
{
    var folder = folders.Take();
    var image = new DirectoryInfo(folder).GetFiles("*.jpg").Shuffle().FirstOrDefault();
    var txt = txts.Take();
    await Tests.TestSmartCrop(
        path: image.FullName,
        primaryText: txt.PrimaryText,
        secondaryText: txt.SecondaryText);
}



Console.WriteLine("Hello, World!");