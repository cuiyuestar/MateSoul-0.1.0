using System;
using System.Collections.Generic;
using MateSoul.Gameplay;
using UnityEngine;

namespace MateSoul.Runtime
{
    public sealed partial class GameSession
    {
        public bool TryCutItem(GameItem target, Plane worldPlane, object owner)
        {
            if (target == null || owner == null || Paused || disposed ||
                target.Instance == null || target.Instance.IsOccupied ||
                !target.Definition.Cuttable.enabled)
                return false;
            var cuttable = target.GetComponent<RuntimeCuttable>();
            if (cuttable == null) return false;

            var runId = Guid.NewGuid();
            var lease = Items.TryReserve(target.Instance, "runtime-cuttable", owner, runId,
                ItemControlState.PlayerUsing);
            if (lease == null) return false;

            RuntimeCutPair pair = null;
            var prepared = new List<GameItem>(2);
            var committed = false;
            try
            {
                var worldPoint = -worldPlane.normal * worldPlane.distance;
                var localNormal = target.transform
                    .InverseTransformDirection(worldPlane.normal).normalized;
                var localPoint = target.transform.InverseTransformPoint(worldPoint);
                var localPlane = new Plane(localNormal, localPoint);
                pair = cuttable.CreateCut(localPlane);
                if (pair == null) return false;

                var pose = target.Body.Pose;
                var inheritedVelocity = target.Body.Rigidbody.velocity;
                var inheritedAngularVelocity = target.Body.Rigidbody.angularVelocity;
                prepared.Add(SpawnCutPiece(target, pair.Positive, pose));
                prepared.Add(SpawnCutPiece(target, pair.Negative, pose));
                Physics.SyncTransforms();

                var positiveShape = prepared[0].Body.Shapes.Count == 1
                    ? prepared[0].Body.Shapes[0]
                    : null;
                var negativeShape = prepared[1].Body.Shapes.Count == 1
                    ? prepared[1].Body.Shapes[0]
                    : null;
                if (!prepared[0].Body.CanOccupy(prepared[0].Body.Pose, .003f,
                        target.transform, ignoredCollider: negativeShape) ||
                    !prepared[1].Body.CanOccupy(prepared[1].Body.Pose, .003f,
                        target.transform, ignoredCollider: positiveShape))
                    throw new InvalidOperationException("切割后的空间被其他物体占用。");

                Items.Remove(target.Instance.Id);
                views.Remove(target.Instance.Id);
                target.Retire();
                UnityEngine.Object.Destroy(target.gameObject);

                var separation = worldPlane.normal.normalized * .035f;
                prepared[0].Body.Rigidbody.velocity = inheritedVelocity + separation;
                prepared[1].Body.Rigidbody.velocity = inheritedVelocity - separation;
                prepared[0].Body.Rigidbody.angularVelocity = inheritedAngularVelocity;
                prepared[1].Body.Rigidbody.angularVelocity = inheritedAngularVelocity;
                committed = true;
                try { Save(); }
                catch (Exception error)
                {
                    Notifications.ShowHint("物品已切开，但保存失败：" + error.Message);
                    RequestPhysicsSave();
                }
                return true;
            }
            catch (Exception error)
            {
                Notifications.ShowHint("切割失败：" + error.Message);
                return false;
            }
            finally
            {
                if (!committed)
                {
                    foreach (var candidate in prepared)
                    {
                        if (candidate == null) continue;
                        Items.Remove(candidate.Instance.Id);
                        views.Remove(candidate.Instance.Id);
                        candidate.Retire();
                        UnityEngine.Object.Destroy(candidate.gameObject);
                    }
                }
                pair?.Dispose();
                if (lease.IsValid) lease.Dispose();
            }
        }

        private GameItem SpawnCutPiece(GameItem source, RuntimeCutPiece piece, Pose pose)
        {
            var definition = source.Definition;
            var id = Guid.NewGuid().ToString();
            var root = UnityEngine.Object.Instantiate(definition.Prefab, itemRoot);
            try
            {
                root.name = definition.DisplayName + "切块";
                root.transform.SetPositionAndRotation(pose.position, pose.rotation);
                var cuttable = root.GetComponent<RuntimeCuttable>();
                if (cuttable == null)
                    throw new InvalidOperationException("可切割物品预制体缺少运行时切割组件。");
                cuttable.ApplyPiece(piece);
                var view = root.AddComponent<GameItem>();
                view.Bind(definition, Items, this, id, "cut",
                    source.InventoryDefinitionId, source.InventoryUnitId);
                views.Add(id, view);
                return view;
            }
            catch
            {
                Items.Remove(id);
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }
    }
}
