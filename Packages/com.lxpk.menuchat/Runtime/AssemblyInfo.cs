using System.Runtime.CompilerServices;

// Grant the test assemblies access to internal members (the JSON builder, the menustate test hook,
// and the scanner's change-detection reset). Unity asmdef assemblies are not strong-name signed, so
// no public key is required.
[assembly: InternalsVisibleTo("com.lxpk.menuchat.tests.editor")]
[assembly: InternalsVisibleTo("com.lxpk.menuchat.tests.runtime")]
