using System.Text;

namespace Preferences.Utils
{
    /// <summary>
    /// About画面に表示する、使用しているライブラリ一覧とライセンス文。
    /// 一覧はCutADash/Common/Preferences/Repositories/SequentialPaste/SelectionToolbar/
    /// WinAPI/Migration各csprojのPackageReferenceを元に手動でまとめたもの
    /// (パッケージが増減した場合はここも合わせて更新すること)。
    ///
    /// 各パッケージのライセンス種別・著作権者は、NuGetの登録API
    /// (https://api.nuget.org/v3/registration5-semver1/{パッケージ名}/index.json の
    /// licenseExpression/licenseUrl)と、そこから辿った配布元リポジトリのLICENSEファイルを
    /// 実際に確認して転記したもの(2026-09-21時点)。
    ///
    /// - Interop.UIAutomationClientは依存していたが、実体はUI Automation COM APIへの
    ///   薄いバインディングでしかなかったため、Common/Infra/Win32/UIAutomationInterop.cs に
    ///   自前のCOM宣言を書いて置き換え、依存から外した(2026-09-19)
    /// - Microsoft.WindowsAppSDKはソースリポジトリ(github.com/microsoft/WindowsAppSDK)
    ///   自体はMITライセンスだが、配布されているNuGetパッケージ(コンパイル済みランタイムを含む)
    ///   はnuget.org上で個別のMicrosoftソフトウェア使用許諾契約として案内されているため、
    ///   ここでは配布物側の扱いに合わせてMicrosoft分類にしている
    /// - Microsoft.Windows.SDK.BuildToolsはnuspecでrequireLicenseAcceptance=trueとなっており、
    ///   定型OSSライセンスではなく個別のMicrosoftライセンス条項に同意する必要があるパッケージ
    /// - Accessibilityパッケージは、csprojでは4.6.0-preview3.19128.7を要求しているが、
    ///   ビルド時の解決結果(NU1603警告)は実際には4.6.0-preview3-27504-2になっており、
    ///   同梱されるのはそちらのバイナリのため、バージョン表記もそれに合わせている
    ///   (nuspecのauthors/copyrightはMicrosoft Corporationで、ライセンス本文自体は
    ///   dotnet/runtimeのLICENSE.TXT(MIT、.NET Foundation and Contributors名義)を指している)
    /// - SQLitePCLRaw.bundle_e_sqlcipher(DB暗号化に使用)は、実行時に
    ///   SQLitePCLRaw.lib.e_sqlcipherパッケージが持つSQLCipherのネイティブバイナリに依存する。
    ///   このネイティブバイナリはSQLitePCLRaw(Eric Sink, Zumero, LLC)とは別のプロジェクト
    ///   (SQLCipher, Zetetic LLC)の成果物であり、SQLitePCLRaw側のApache-2.0ラベルは
    ///   同梱されたSQLCipher本体を再ライセンスするものではないため、SQLCipher自体を
    ///   別エントリ(BSD-3-Clause, Copyright (c) Zetetic LLC)として明記する
    /// - 絵文字画像(Assets/Emoji/*.png)はNuGetパッケージではなく、AssetsSrc配下に
    ///   git submoduleとして追加したmicrosoft/fluentui-emoji(MIT、Copyright (c) Microsoft
    ///   Corporation)の3Dスタイルアセットを、ビルド前にAssetsSrc/EmojiGenerateScript/
    ///   build_emoji_data.pyでスプライトシート化して同梱したもの。以前試したSegoe UI Emoji
    ///   フォントからの実行時レンダリング方式(フォントEULA上、画像として配布不可)から切り替えた
    /// </summary>
    internal static class OpenSourceLicenses
    {
        private sealed record Package(string Name, string Version, string License, string Copyright, string Url);

        private static readonly Package[] Packages =
        {
            new("Accessibility", "4.6.0-preview3-27504-2", "MIT", ".NET Foundation and Contributors", "https://github.com/dotnet/runtime"),
            new("CommunityToolkit.Mvvm", "8.4.2", "MIT", ".NET Foundation and Contributors", "https://github.com/CommunityToolkit/dotnet"),
            // 絵文字画像の情報源(NuGetパッケージではなくgit submodule)。詳細は上のクラスコメント参照
            new("fluentui-emoji", "(submodule)", "MIT", "(c) Microsoft Corporation", "https://github.com/microsoft/fluentui-emoji"),
            new("Microsoft.Extensions.DependencyInjection", "10.0.9", "MIT", ".NET Foundation and Contributors", "https://github.com/dotnet/runtime"),
            new("Microsoft.Windows.SDK.BuildTools", "10.0.28000.1721", "Microsoft", "(c) Microsoft Corporation", "https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools"),
            new("Microsoft.WindowsAppSDK", "1.8.260416003", "Microsoft", "(c) Microsoft Corporation", "https://github.com/microsoft/WindowsAppSDK"),
            new("Microsoft.Xaml.Behaviors.WinUI.Managed", "3.0.1", "MIT", "(c) 2015 Microsoft", "https://github.com/microsoft/XamlBehaviors"),
            new("Newtonsoft.Json", "13.0.4", "MIT", "(c) 2007 James Newton-King", "https://github.com/JamesNK/Newtonsoft.Json"),
            new("SQLitePCLRaw.bundle_e_sqlcipher", "2.1.11", "Apache-2.0", "(c) Eric Sink, Zumero, LLC and Contributors", "https://github.com/ericsink/SQLitePCL.raw"),
            new("SQLitePCLRaw.core", "2.1.11", "Apache-2.0", "(c) Eric Sink, Zumero, LLC and Contributors", "https://github.com/ericsink/SQLitePCL.raw"),
            new("SQLitePCLRaw.lib.e_sqlite3", "2.1.13", "Apache-2.0", "(c) Eric Sink, Zumero, LLC and Contributors", "https://github.com/ericsink/SQLitePCL.raw"),
            // SQLitePCLRaw.bundle_e_sqlcipher経由で実行時に読み込まれるネイティブバイナリの
            // 実体。SQLitePCLRaw自体とは別プロジェクト(別著作権者)のため独立して掲載する
            new("SQLCipher", "(SQLitePCLRaw.lib.e_sqlcipher 2.1.11 に同梱)", "BSD-3-Clause", "(c) Zetetic LLC", "https://github.com/sqlcipher/sqlcipher"),
            new("System.Drawing.Common", "8.0.10", "MIT", ".NET Foundation and Contributors", "https://github.com/dotnet/runtime"),
            new("System.Security.Cryptography.ProtectedData", "8.0.0", "MIT", ".NET Foundation and Contributors", "https://github.com/dotnet/runtime"),
            new("Velopack", "0.0.1298", "MIT", "(c) Velopack Ltd", "https://github.com/velopack/velopack"),
            new("WinUIEx", "2.9.0", "MIT", "(c) 2021 Morten Nielsen", "https://github.com/dotmorten/WinUIEx"),
            new("ZXing.Net", "0.16.10", "Apache-2.0", "(c) 2007-2009 ZXing authors / Michael Jahn (ZXing.Net)", "https://github.com/micjahn/ZXing.Net"),
            new("ZXing.Net.Bindings.Windows.Compatibility", "0.16.12", "Apache-2.0", "(c) 2007-2009 ZXing authors / Michael Jahn (ZXing.Net)", "https://github.com/micjahn/ZXing.Net"),
            new("sqlite-net-pcl", "1.9.172", "MIT", "(c) Krueger Systems, Inc.", "https://github.com/praeclarum/sqlite-net"),
        };

        private const string MitLicenseUrl = "https://opensource.org/license/mit/";
        private const string ApacheLicenseUrl = "https://www.apache.org/licenses/LICENSE-2.0";
        private const string MicrosoftLicenseNote =
            "Microsoftソフトウェア使用許諾契約(MIT/Apache等の定型OSSライセンスではない個別の条項)。" +
            "詳細はNuGetパッケージページのライセンスリンクを参照してください。" +
            "なお、Microsoft.WindowsAppSDKはソースコード自体はMITで公開されているが、" +
            "配布されるNuGetパッケージ(コンパイル済みランタイム込み)は別条項での提供となっている。";

        private const string MitLicenseTextTemplate =
@"MIT License

Copyright {0}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.";

        private const string ApacheLicenseText =
@"Apache License
Version 2.0, January 2004
https://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.

""License"" shall mean the terms and conditions for use, reproduction, and
distribution as defined by Sections 1 through 9 of this document.

""Licensor"" shall mean the copyright owner or entity authorized by the
copyright owner that is granting the License.

""You"" (or ""Your"") shall mean an individual or Legal Entity exercising
permissions granted by this License.

""Source"" form shall mean the preferred form for making modifications,
including but not limited to software source code, documentation source,
and configuration files.

""Object"" form shall mean any form resulting from mechanical transformation
or translation of a Source form, including but not limited to compiled
object code, generated documentation, and conversions to other media types.

""Work"" shall mean the work of authorship, whether in Source or Object form,
made available under the License, as indicated by a copyright notice that
is included in or attached to the work.

2. Grant of Copyright License. Subject to the terms and conditions of this
License, each Contributor hereby grants to You a perpetual, worldwide,
non-exclusive, no-charge, royalty-free, irrevocable copyright license to
reproduce, prepare Derivative Works of, publicly display, publicly perform,
sublicense, and distribute the Work and such Derivative Works in Source or
Object form.

3. Grant of Patent License. Subject to the terms and conditions of this
License, each Contributor hereby grants to You a perpetual, worldwide,
non-exclusive, no-charge, royalty-free, irrevocable (except as stated in
this section) patent license to make, have made, use, offer to sell, sell,
import, and otherwise transfer the Work.

4. Redistribution. You may reproduce and distribute copies of the Work or
Derivative Works thereof in any medium, with or without modifications, and
in Source or Object form, provided that You meet the conditions stated in
the full license text.

5. Submission of Contributions. Unless You explicitly state otherwise, any
Contribution intentionally submitted for inclusion in the Work by You to the
Licensor shall be under the terms and conditions of this License.

6. Trademarks. This License does not grant permission to use the trade
names, trademarks, service marks, or product names of the Licensor.

7. Disclaimer of Warranty. Unless required by applicable law or agreed to in
writing, Licensor provides the Work on an ""AS IS"" BASIS, WITHOUT WARRANTIES
OR CONDITIONS OF ANY KIND, either express or implied.

8. Limitation of Liability. In no event and under no legal theory shall any
Contributor be liable to You for damages arising as a result of this License.

9. Accepting Warranty or Additional Liability. You may choose to offer, and
charge a fee for, acceptance of support, warranty, indemnity, or other
liability obligations consistent with this License.

(全文は https://www.apache.org/licenses/LICENSE-2.0 を参照してください)";

        private const string BsdLicenseUrl = "https://github.com/sqlcipher/sqlcipher/blob/master/LICENSE.md";

        // BSD-3-Clauseの標準テンプレート。SQLCipher(Zetetic LLC)のLICENSE.mdに著作権者を
        // 当てはめたもの。正確な最新の条文は上記URL(配布元リポジトリ)で必ず確認すること
        private const string BsdLicenseTextTemplate =
@"BSD 3-Clause License

Copyright {0}. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS ""AS IS""
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.";

        public static string BuildLicensesText()
        {
            var sb = new StringBuilder();

            sb.AppendLine("使用しているオープンソースライブラリ");
            sb.AppendLine();

            for (var i = 0; i < Packages.Length; i++)
            {
                var pkg = Packages[i];

                sb.AppendLine($"{pkg.Name} ({pkg.Url})");
                sb.AppendLine($"Version {pkg.Version} - Copyright {pkg.Copyright}");
                sb.AppendLine();
                sb.AppendLine(BuildLicenseText(pkg));

                if (i < Packages.Length - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine("----------------------------------------");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string BuildLicenseText(Package pkg) => pkg.License switch
        {
            "MIT" => string.Format(MitLicenseTextTemplate, pkg.Copyright) + $"\n\n(ライセンス原文: {MitLicenseUrl})",
            "Apache-2.0" => ApacheLicenseText + $"\n\n(ライセンス原文: {ApacheLicenseUrl})",
            "BSD-3-Clause" => string.Format(BsdLicenseTextTemplate, pkg.Copyright) + $"\n\n(ライセンス原文: {BsdLicenseUrl})",
            "Microsoft" => MicrosoftLicenseNote,
            _ => pkg.License,
        };
    }
}
