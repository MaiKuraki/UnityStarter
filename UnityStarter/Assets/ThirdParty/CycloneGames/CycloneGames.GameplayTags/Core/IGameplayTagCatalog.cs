namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A statically declared, reflection-free description of the gameplay tags one unit of code contributes.
   /// </summary>
   /// <remarks>
   /// <para>
   /// An implementation is either written by hand or emitted by a build step that already knows the tag
   /// list. Either way the tags arrive as ordinary executable code, so the registry never has to walk
   /// <see cref="System.AppDomain"/> or call <see cref="System.Reflection.Assembly.GetCustomAttributes"/>.
   /// That matters in three places at once:
   /// </para>
   /// <list type="bullet">
   /// <item><description>
   /// AOT and IL2CPP - type discovery through attribute sweeping is exactly the pattern the managed
   /// stripper cannot see through, so attributes survive compilation only by luck.
   /// </description></item>
   /// <item><description>
   /// HybridCLR hot update - a hot-loaded assembly never participates in
   /// <see cref="System.AppDomain.CurrentDomain"/> enumeration reliably enough to be a registry input,
   /// and the Player path does not scan assemblies at all.
   /// </description></item>
   /// <item><description>
   /// Startup cost - a generated <c>Collect</c> call is a straight-line sequence of
   /// <see cref="GameplayTagCatalogBuilder.Add"/> invocations with no metadata walk.
   /// </description></item>
   /// </list>
   /// <para>
   /// Catalogs are registered explicitly. .NET Standard 2.1 has no module initializer, and even where one
   /// exists, implicit self-registration would make registry contents depend on which assemblies happened
   /// to have been touched - which is precisely the nondeterminism this module exists to remove.
   /// </para>
   /// </remarks>
   public interface IGameplayTagCatalog
   {
      /// <summary>A stable name for diagnostics, normally the sanitized source assembly name.</summary>
      string Name { get; }

      /// <summary>Writes every tag declared by this catalog into <paramref name="builder"/>.</summary>
      void Collect(GameplayTagCatalogBuilder builder);
   }
}
