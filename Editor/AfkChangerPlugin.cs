using nadena.dev.ndmf;
using Sebanne.AfkChanger;

[assembly: ExportsPlugin(typeof(Sebanne.AfkChanger.Editor.AfkChangerPlugin))]

namespace Sebanne.AfkChanger.Editor
{
    public sealed class AfkChangerPlugin : Plugin<AfkChangerPlugin>
    {
        public override string DisplayName => "AFK Changer";
        public override string QualifiedName => "com.sebanne.afk-changer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run("Replace AFK states", ctx =>
                {
                    // TODO: AFK ステート入れ替えロジック
                });
        }
    }
}
