using System.Runtime.CompilerServices;

// Lets the PlayMode tests and the Editor tools reach the internal types in this
// assembly — Systems_UiFitAudit above all, which the aspect sweep runs against
// every screen at every ratio.
//
// The same reasoning as Agent_AssemblyInfo: a test or an Editor tool is the same
// code, checked, not a consumer that justifies widening a type to public.
// Assembly-CSharp-Editor is Assets/Editor/, which has no asmdef of its own.
[assembly: InternalsVisibleTo("PoFootball.Tests.PlayMode")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
