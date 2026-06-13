namespace Orderly.Core.Commerce;

/// <summary>
/// Base for system / configuration entities that are NOT scoped to a single workspace and
/// therefore carry no <c>WorkspaceId</c> (Req 2.2). The <c>BusinessWorkspace</c> scoping root
/// itself, along with system-level template, unit, and other configuration entities, extend
/// this type and rely solely on the shared <see cref="CommerceEntity"/> audit, lifecycle, and
/// personalization surface.
/// </summary>
public abstract class SystemEntity : CommerceEntity
{
}
