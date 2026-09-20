path = r"D:/unity/plan go/Musical Sprite/Assets/Scripts/HoldNote.cs"

with open(path, "rb") as f:
    raw = f.read()
data = raw.decode("utf-8")
eol = "\r\n" if "\r\n" in data else "\n"

def conv(s):
    return s.replace("\n", eol)

reps = []

old1 = conv("""        int M = vs.Length / 2 - 1;
        for (int i = 0; i <= M; i++)
        {
            Vector3 vp = vs[2 * i];
            float rev = side == 0 ? Smoothstep(localRevealX + bandRevealWidth, localRevealX, vp.x) : Smoothstep(localRevealX - bandRevealWidth, localRevealX, vp.x);
            float fade = side == 0 ? Smoothstep(localJudgeX - bandFadeWidth, localJudgeX, vp.x) : Smoothstep(localJudgeX + bandFadeWidth, localJudgeX, vp.x);
            float alpha = rev * fade * g;
            cols[2 * i].a = alpha;
            cols[2 * i + 1].a = alpha;
        }
        mesh.colors = cols;""")
new1 = conv("""        int M = vs.Length / 2 - 1;
        // fen gang reveal gradient to crossed side
        float odSign = (spawnPositions.Length > 0 && spawnPositions[0].x > judgeX) ? 1f : -1f;
        for (int i = 0; i <= M; i++)
        {
            Vector3 vp = vs[2 * i];
            float rev = Smoothstep(localRevealX, localRevealX - odSign * bandRevealWidth, vp.x);
            float fade = side == 0 ? Smoothstep(localJudgeX - bandFadeWidth, localJudgeX, vp.x) : Smoothstep(localJudgeX + bandFadeWidth, localJudgeX, vp.x);
            float alpha = rev * fade * g;
            cols[2 * i].a = alpha;
            cols[2 * i + 1].a = alpha;
        }
        mesh.colors = cols;""")
reps.append((old1, new1, "rev-gradient"))

old2 = "    private void RebuildRibbon(int s, float u0, float u1, Mesh mesh, float judgeX, float cumStart, float totalLen, float yDip)"
new2 = "    private void RebuildRibbon(int s, float u0, float u1, Mesh mesh, float judgeX, float cumStart, float totalLen, float yDip, float coverWorldX = float.NaN)"
reps.append((old2, new2, "signature"))

old3 = conv("""        for (int k = 0; k < verts.Length; k++) verts[k] = transform.InverseTransformPoint(verts[k]);
        mesh.vertices = verts;""")
new3 = conv("""        for (int k = 0; k < verts.Length; k++) verts[k] = transform.InverseTransformPoint(verts[k]);
        if (!float.IsNaN(coverWorldX))
        {
            float localLeadX = transform.InverseTransformPoint(new Vector3(coverWorldX, 0f, 0f)).x;
            float ax = a.x, bx = b.x;
            float uCut = (Mathf.Abs(bx - ax) > 1e-5f) ? (coverWorldX - ax) / (bx - ax) : float.NaN;
            if (!float.IsNaN(uCut))
            {
                if (Mathf.Abs(u0 - uCut) < 1e-3f) { verts[0].x = localLeadX; verts[1].x = localLeadX; }
                if (Mathf.Abs(u1 - uCut) < 1e-3f) { verts[2 * M].x = localLeadX; verts[2 * M + 1].x = localLeadX; }
            }
        }
        mesh.vertices = verts;""")
reps.append((old3, new3, "ribbon-cut"))

old4 = conv("""                float uA = (dA - od0) / (od1 - od0);
                float uB = (dB - od0) / (od1 - od0);
                RebuildRibbon(s, Mathf.Min(uA, uB), Mathf.Max(uA, uB), bandOverlayMeshes[s], judgeX, cum[s], total, bandRideYDip + 0.005f);""")
new4 = conv("""                float uA = (dA - od0) / (od1 - od0);
                float uB = (dB - od0) / (od1 - od0);
                float targetX = judgeLineX + odSign * cover;
                RebuildRibbon(s, Mathf.Min(uA, uB), Mathf.Max(uA, uB), bandOverlayMeshes[s], judgeX, cum[s], total, bandRideYDip + 0.005f, targetX);""")
reps.append((old4, new4, "overlay-call"))

for old, new, name in reps:
    cnt = data.count(old)
    if cnt != 1:
        print("FAIL [%s]: expected 1 match, found %d" % (name, cnt))
        import sys
        sys.exit(1)
    data = data.replace(old, new)
    print("OK   [%s]" % name)

with open(path, "wb") as f:
    f.write(data.encode("utf-8"))
print("DONE")
