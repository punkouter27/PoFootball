#ifndef POFOOTBALL_PLAYER_BODY_INCLUDED
#define POFOOTBALL_PLAYER_BODY_INCLUDED

// Shared body shading for PoFootball/Player, used by both the lit and unlit
// passes so the two cannot drift.
//
// HOW THE EDGE IS FOUND. The role shapes are solid silhouettes with a hard
// alpha edge and no distance field, so there is nothing to read "how far am I
// from the rim" out of directly. Blurring the alpha over a small ring gives one
// for free: the average is 1.0 deep inside the shape, falls through 0.5 exactly
// at the edge, and reaches 0 outside. Everything below — outline, bevel, rim
// glow — is a threshold on that one number.
//
// THE OUTLINE GROWS INWARD. It is drawn inside the existing silhouette, never
// outside it. Systems_RoleShapeApplier is explicit that a player's sprite is
// exactly as big as its 0.5 m collider so the picture never lies about where
// contact happens; an outline grown outward would quietly add a few centimetres
// of visual body that nothing can be tackled by.

// Ring offsets, unit length. Eight directions is enough for shapes this simple —
// a circle, a square, a hexagon — and keeps this to seventeen taps.
static const float2 POFOOTBALL_RING[8] =
{
    float2( 1.0,  0.0), float2( 0.7071,  0.7071),
    float2( 0.0,  1.0), float2(-0.7071,  0.7071),
    float2(-1.0,  0.0), float2(-0.7071, -0.7071),
    float2( 0.0, -1.0), float2( 0.7071, -0.7071)
};

// Alpha of one tap, treating anything outside the sprite's UV rectangle as
// empty.
//
// THIS IS LOAD-BEARING FOR THE SQUARE. A circle or a hexagon has transparent
// corners, so a blur naturally finds its edge somewhere inside the texture. The
// lineman's square fills its 256 px source right to the border, so every tap
// lands on alpha 1, the clamped wrap mode returns alpha 1 outside as well, and
// the shape reads as having no edge at all — perfectly flat, exactly like the
// four other role shapes it is supposed to be distinguishable from. Counting
// out-of-bounds as empty gives the square the same silhouette every other shape
// gets for free.
float PlayerTapAlpha(float2 uv)
{
    float inside = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(uv)).a * inside;
}

// Average alpha over two concentric rings plus the centre tap.
float PlayerEdgeCoverage(float2 uv, float radius)
{
    float total = PlayerTapAlpha(uv);
    float weight = 1.0;

    [unroll]
    for (int index = 0; index < 8; index++)
    {
        float2 direction = POFOOTBALL_RING[index];

        total += PlayerTapAlpha(uv + direction * radius);
        total += PlayerTapAlpha(uv + direction * radius * 0.55) * 1.5;
        weight += 2.5;
    }

    return total / weight;
}

// Where the stadium's casting key light is, in world space. Set once by
// Systems_StadiumRigView: xy is the light's position, w is 1 once a rig has set
// it. A global and not a material property, so the SRP batcher's per-material
// layout is untouched, and there is one key for every player.
float4 _PoFootballKeyLight;

// Unit direction from this body toward the key, in the sprite's own UV frame.
//
// WHY THIS AND NOT A NORMAL MAP. The URP 2D lights only read a normal buffer when
// normal mapping is switched on per light, and then they multiply EVERY surface
// by N.L — the flat turf included, which takes the field's measured
// ambient-to-bank ratio apart (docs/PRESENTATION.md). The bevel below already
// has the shape's relief in it; what it lacked was the light. It used to lift
// the sprite's own +Y, which rotates with the body, so a player turning round
// turned his highlight round with him. Aimed at the key it stays on the side
// the shadow does not fall on, however he is facing.
//
// Falls back to +Y — exactly the old bevel — when no rig has published a key,
// which is training and the menu.
half2 PlayerKeyDirection()
{
    if (_PoFootballKeyLight.w < 0.5)
    {
        return half2(0.0, 1.0);
    }

    float3 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float2 toKey = _PoFootballKeyLight.xy - originWS.xy;

    // Object space is the sprite's frame; a flipped sprite mirrors its UVs.
    float2 direction = TransformWorldToObjectDir(float3(toKey, 0.0), false).xy
        * unity_SpriteProps.xy;

    float length2 = dot(direction, direction);
    return length2 > 1e-8 ? (half2)(direction * rsqrt(length2)) : half2(0.0, 1.0);
}

half3 PlayerDesaturate(half3 color, float amount)
{
    // Rec. 709 luma. A flat average would turn the blue offense and the red
    // defense into two different greys, which is the one thing the tint exists
    // to prevent.
    float luma = dot(color, half3(0.2126, 0.7152, 0.0722));
    return lerp(color, luma.xxx, amount);
}

half4 PlayerBody(float2 uv, half4 tint, half2 keyDirection)
{
    float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;

    // UV-relative rather than texel-relative, so all eight role shapes get a
    // proportionally identical rim regardless of their import resolution.
    //
    // The 0.32 is set against how big a player actually is on screen, not
    // against the source art. A body is a 1 m circle on a 110 m field in a
    // portrait window — roughly thirty pixels across. An outline sized as a
    // sensible fraction of a 256 px sprite lands at half a pixel and simply is
    // not there, which is what the first version of this shader shipped.
    float radius = _OutlineWidth * 0.32;
    float coverage = PlayerEdgeCoverage(uv, radius);

    // 0 at the silhouette edge, 1 well inside it.
    float inner = smoothstep(0.45, 0.92, coverage);

    // --- Fill ---------------------------------------------------------------
    half3 fill = tint.rgb;
    fill = PlayerDesaturate(fill, saturate(_Fatigue) * _FatigueDesaturation);

    // Fatigue also drops the value a little. Desaturation alone reads as a
    // lighting change; darkening is what makes it read as effort.
    fill *= 1.0 - saturate(_Fatigue) * 0.25;

    // Bevel: dark at the rim, lifted through the middle. The lift is applied
    // off-centre toward the key light, so the shading agrees with the shadow
    // the same light throws (see PlayerKeyDirection).
    float2 fromCentre = uv - 0.5;
    float shoulder = smoothstep(0.35, 1.0, coverage + dot(fromCentre, keyDirection) * 0.35);
    float shade = lerp(1.0 - _BevelDepth, 1.0 + _BevelLift, shoulder);

    // Relief: the band just inside the outline catches the key on the near side
    // and falls into shade on the far one. Radial rather than a true gradient —
    // for a circle they are the same, and for the square the error is at the
    // corners, which are four pixels of a thirty-pixel body.
    //
    // IT CANNOT RAISE THE BRIGHTEST PIXEL. The band sits where the bevel has
    // already darkened the fill to about 0.6-0.8; the most this lifts it is
    // 1 + 0.2 * _Relief, which stays under the centre's 1 + _BevelLift. The bloom
    // threshold in Systems_PostProcessView was tuned against that peak and the
    // carrier rim above it, and neither moves.
    float band = smoothstep(0.45, 0.70, coverage) * (1.0 - smoothstep(0.70, 0.97, coverage));
    float radialLength2 = dot(fromCentre, fromCentre);
    float facing = radialLength2 > 1e-6
        ? dot(fromCentre * rsqrt(radialLength2), keyDirection)
        : 0.0;
    shade *= 1.0 + band * facing * 0.2 * _Relief;

    fill *= shade;

    // --- Outline ------------------------------------------------------------
    float outline = saturate(1.0 - inner);
    half3 body = lerp(fill, _OutlineColor.rgb, outline * _OutlineColor.a);

    // --- Carrier rim --------------------------------------------------------
    // A band sitting just inside the outline rather than on top of it, so the
    // glow reads as light coming off the body instead of a recoloured edge.
    float carrier = saturate(_Carrier);
    if (carrier > 0.0)
    {
        float band = smoothstep(0.30, 0.62, coverage) * (1.0 - smoothstep(0.62, 0.95, coverage));

        // Two-rate pulse. A single sine is a slow throb that the eye stops
        // tracking; a faster component on top keeps it alive in a crowd.
        float pulse = 0.62
                    + 0.26 * sin(_Time.y * 7.5)
                    + 0.12 * sin(_Time.y * 19.0);

        body += _CarrierColor.rgb * band * pulse * carrier * 1.35;
    }

    // --- Impact -------------------------------------------------------------
    // The two bodies in a collision, for about a fifth of a second after it.
    //
    // FILL AND RIM TOGETHER, not one or the other. Lifting only the rim reads as
    // a selection highlight — the same language the carrier glow above already
    // owns, and putting a second meaning on it would make neither legible. Lifting
    // only the fill reads as a team colour changing. Blowing out both at once is
    // what reads as a body being hit, and it survives a pile-up, which is the only
    // place tackles in this simulation actually happen.
    //
    // The amount is closing speed, normalized over exactly the range
    // Systems_AudioView scales the impact sound by, decayed by
    // Systems_PlayerAppearanceView. A hard hit whitens the whole shape; a glancing
    // one barely registers, which is correct — it barely was one.
    float impact = saturate(_Impact);
    if (impact > 0.0)
    {
        float rim = 1.0 - smoothstep(0.30, 0.75, coverage);

        body = lerp(body, _ImpactColor.rgb, impact * 0.55);
        body += _ImpactColor.rgb * rim * impact * 1.6;
    }

    return half4(body, alpha * tint.a);
}

#endif // POFOOTBALL_PLAYER_BODY_INCLUDED
