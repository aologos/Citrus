using System;
using System.Collections;
using System.Collections.Generic;
using Yuzu;

namespace Lime
{
	public interface IAnimator : IDisposable
	{
		IAnimationHost Owner { get; set; }

		IAnimable Animable { get; set; }

		IAnimator Next { get; set; }

		IAnimator Clone();

		bool IsTriggerable { get; set; }

		string TargetPropertyPath { get; set; }

		string AnimationId { get; set; }

		bool Enabled { get; set; }

		int Duration { get; }

		void InvokeTrigger(int frame, double animationTimeCorrection = 0);

		void Apply(double time);

		void ResetCache();

		IKeyframeList ReadonlyKeys { get; }

		IKeyframeList Keys { get; }

		object UserData { get; set; }

		Type GetValueType();

		bool TryGetNextKeyFrame(int nextFrame, out int keyFrame);

		void Unbind();

		bool IsZombie { get; }
	}

	public interface IKeyframeList : IList<IKeyframe>
	{
		IKeyframe CreateKeyframe();

		IKeyframe GetByFrame(int frame);

		void Add(int frame, object value, KeyFunction function = KeyFunction.Linear);
		void AddOrdered(int frame, object value, KeyFunction function = KeyFunction.Linear);
		void AddOrdered(IKeyframe keyframe);

		int Version { get; }
	}

	public class Animator<T> : IAnimator
	{
		public IAnimationHost Owner { get; set; }
		public IAnimable Animable { get; set; }
		public IAnimator Next { get; set; }

		private double minTime;
		private double maxTime;
		private KeyFunction function;
		private int keyIndex;
		protected T Value1, Value2, Value3, Value4;

		public bool IsTriggerable { get; set; }
		public bool Enabled { get; set; } = true;
		private delegate void SetterDelegate(T value);
		private delegate void IndexedSetterDelegate(int index, T value);
		private SetterDelegate setter;
		public bool IsZombie { get; private set; }

		[YuzuMember("TargetProperty")]
		public string TargetPropertyPath { get; set; }

		public Type GetValueType() { return typeof(T); }

		[YuzuMember]
		public TypedKeyframeList<T> ReadonlyKeys { get; private set; }

		[YuzuMember]
		public string AnimationId { get; set; }

		public object UserData { get; set; }

		public Animator()
		{
			ReadonlyKeys = new TypedKeyframeList<T>();
			ReadonlyKeys.AddRef();
		}

		public void Dispose()
		{
			ReadonlyKeys.Release();
		}

		public TypedKeyframeList<T> Keys
		{
			get
			{
				if (ReadonlyKeys.RefCount > 1) {
					ReadonlyKeys.Release();
					ReadonlyKeys = ReadonlyKeys.Clone();
					ReadonlyKeys.AddRef();
				}
				return ReadonlyKeys;
			}
		}

		IKeyframeList boxedKeys;
		IKeyframeList IAnimator.Keys
		{
			get
			{
				if (ReadonlyKeys.RefCount > 1) {
					boxedKeys = null;
				}
				if (boxedKeys == null) {
					boxedKeys = new BoxedKeyframeList<T>(Keys);
				}
				return boxedKeys;
			}
		}

		IKeyframeList IAnimator.ReadonlyKeys
		{
			get
			{
				if (boxedKeys == null) {
					boxedKeys = new BoxedKeyframeList<T>(ReadonlyKeys);
				}
				return boxedKeys;
			}
		}

		public IAnimator Clone()
		{
			var clone = (Animator<T>)MemberwiseClone();
			clone.setter = null;
			clone.Animable = null;
			clone.IsZombie = false;
			clone.Owner = null;
			clone.Next = null;
			clone.boxedKeys = null;
			boxedKeys = null;
			ReadonlyKeys.AddRef();
			return clone;
		}

		public void Unbind()
		{
			IsZombie = false;
			setter = null;
			Animable = null;
		}

		public int Duration => (ReadonlyKeys.Count == 0) ? 0 : ReadonlyKeys[ReadonlyKeys.Count - 1].Frame;

		protected virtual T InterpolateLinear(float t) => Value2;
		protected virtual T InterpolateSplined(float t) => InterpolateLinear(t);

		public void Clear()
		{
			keyIndex = 0;
			Keys.Clear();
		}

		public void InvokeTrigger(int frame, double animationTimeCorrection = 0)
		{
			if (ReadonlyKeys.Count > 0 && Enabled) {
				// This function relies on currentKey value. Therefore Apply(time) must be called before.
				if (ReadonlyKeys[keyIndex].Frame == frame) {
					Owner.OnTrigger(TargetPropertyPath, animationTimeCorrection);
				}
			}
		}

		public void Apply(double time)
		{
			if (Enabled && !IsZombie) {
				if (setter == null) {
					Bind();
					if (IsZombie) {
						return;
					}
				}
				setter(CalcValue(time));
			}
		}

		private void Bind()
		{
			var (p, animable, index) = AnimationUtils.GetPropertyByPath(Owner, TargetPropertyPath);
			var mi = p.Info?.GetSetMethod();
			IsZombie = animable == null || mi == null || p.Info.PropertyType != typeof(T) || animable is IList list && index >= list.Count;
			if (IsZombie) {
				return;
			}
			Animable = animable;
			IsTriggerable = p.Triggerable;
			if (index == -1) {
				setter = (SetterDelegate)Delegate.CreateDelegate(typeof(SetterDelegate), animable, mi);
			} else {
				var indexedSetter = (IndexedSetterDelegate)Delegate.CreateDelegate(typeof(IndexedSetterDelegate), animable, mi);
				setter = (v) => {
					indexedSetter(index, v);
				};
			}
		}

		public void ResetCache()
		{
			minTime = maxTime = 0;
		}

		public T CalcValue(double time)
		{
			if (time < minTime || time >= maxTime) {
				CacheInterpolationParameters(time);
			}
			if (function == KeyFunction.Steep) {
				return Value2;
			}
			var t = (float)((time - minTime) / (maxTime - minTime));
			if (function == KeyFunction.Linear) {
				return InterpolateLinear(t);
			} else {
				return InterpolateSplined(t);
			}
		}

		public bool TryGetNextKeyFrame(int nextFrame, out int keyFrame)
		{
			foreach (var key in ReadonlyKeys) {
				if (key.Frame < nextFrame) {
					continue;
				}

				keyFrame = key.Frame;
				return true;
			}
			keyFrame = -1;
			return false;
		}

		private void CacheInterpolationParameters(double time)
		{
			int count = ReadonlyKeys.Count;
			if (count == 0) {
				Value2 = default(T);
				minTime = -float.MaxValue;
				maxTime = float.MaxValue;
				function = KeyFunction.Steep;
				return;
			}
			var i = keyIndex;
			if (i >= count) {
				i = count - 1;
			}
			int frame = AnimationUtils.SecondsToFrames(time);
			// find rightmost key on the left from the given frame
			while (i < count - 1 && frame > ReadonlyKeys[i].Frame) {
				i++;
			}
			while (i >= 0 && frame < ReadonlyKeys[i].Frame) {
				i--;
			}
			keyIndex = i;
			int minFrame, maxFrame;
			if (i < 0) {
				keyIndex = 0;
				maxFrame = ReadonlyKeys[0].Frame;
				minFrame = int.MinValue;
				Value2 = ReadonlyKeys[0].Value;
				function = KeyFunction.Steep;
			} else if (i == count - 1) {
				minFrame = ReadonlyKeys[i].Frame;
				maxFrame = int.MaxValue;
				Value2 = ReadonlyKeys[i].Value;
				function = KeyFunction.Steep;
			} else {
				var key1 = ReadonlyKeys[i];
				var key2 = ReadonlyKeys[i + 1];
				minFrame = key1.Frame;
				maxFrame = key2.Frame;
				Value2 = key1.Value;
				Value3 = key2.Value;
				function = key1.Function;
				if (function == KeyFunction.Spline) {
					Value1 = ReadonlyKeys[i < 1 ? 0 : i - 1].Value;
					Value4 = ReadonlyKeys[i + 1 >= count - 1 ? count - 1 : i + 2].Value;
				} else if (function == KeyFunction.ClosedSpline) {
					Value1 = ReadonlyKeys[i < 1 ? count - 2 : i - 1].Value;
					Value4 = ReadonlyKeys[i + 1 >= count - 1 ? 1 : i + 2].Value;
				}
			}
			minTime = minFrame * AnimationUtils.SecondsPerFrame;
			maxTime = maxFrame * AnimationUtils.SecondsPerFrame;
		}
	}

	public class Vector2Animator : Animator<Vector2>
	{
		protected override Vector2 InterpolateLinear(float t)
		{
			Vector2 r;
			r.X = Value2.X + (Value3.X - Value2.X) * t;
			r.Y = Value2.Y + (Value3.Y - Value2.Y) * t;
			return r;
		}

		protected override Vector2 InterpolateSplined(float t)
		{
			return new Vector2(
				Mathf.CatmullRomSpline(t, Value1.X, Value2.X, Value3.X, Value4.X),
				Mathf.CatmullRomSpline(t, Value1.Y, Value2.Y, Value3.Y, Value4.Y)
			);
		}
	}

	public class Vector3Animator : Animator<Vector3>
	{
		protected override Vector3 InterpolateLinear(float t)
		{
			return Vector3.Lerp(t, Value2, Value3);
		}

		protected override Vector3 InterpolateSplined(float t)
		{
			return Mathf.CatmullRomSpline(t, Value1, Value2, Value3, Value4);
		}
	}

	public class NumericAnimator : Animator<float>
	{
		protected override float InterpolateLinear(float t)
		{
			return t * (Value3 - Value2) + Value2;
		}

		protected override float InterpolateSplined(float t)
		{
			return Mathf.CatmullRomSpline(t, Value1, Value2, Value3, Value4);
		}
	}

	public class IntAnimator : Animator<int>
	{
		protected override int InterpolateLinear(float t)
		{
			return (t * (Value3 - Value2) + Value2).Round();
		}

		protected override int InterpolateSplined(float t)
		{
			return Mathf.CatmullRomSpline(t, Value1, Value2, Value3, Value4).Round();
		}
	}

	public class Color4Animator : Animator<Color4>
	{
		protected override Color4 InterpolateLinear(float t)
		{
			return Color4.Lerp(t, Value2, Value3);
		}
	}

	public class QuaternionAnimator : Animator<Quaternion>
	{
		protected override Quaternion InterpolateLinear(float t)
		{
			return Quaternion.Slerp(Value2, Value3, t);
		}
	}

	public class Matrix44Animator : Animator<Matrix44>
	{
		protected override Matrix44 InterpolateLinear(float t)
		{
			return Matrix44.Lerp(Value2, Value3, t);
		}
	}

	public class ThicknessAnimator : Animator<Thickness>
	{
		protected override Thickness InterpolateLinear(float t)
		{
			Thickness r;
			r.Left = Value2.Left + (Value3.Left - Value2.Left) * t;
			r.Right = Value2.Right + (Value3.Right - Value2.Right) * t;
			r.Top = Value2.Top + (Value3.Top - Value2.Top) * t;
			r.Bottom = Value2.Bottom + (Value3.Bottom - Value2.Bottom) * t;
			return r;
		}

		protected override Thickness InterpolateSplined(float t)
		{
			return new Thickness(
				Mathf.CatmullRomSpline(t, Value1.Left, Value2.Left, Value3.Left, Value4.Left),
				Mathf.CatmullRomSpline(t, Value1.Right, Value2.Right, Value3.Right, Value4.Right),
				Mathf.CatmullRomSpline(t, Value1.Top, Value2.Top, Value3.Top, Value4.Top),
				Mathf.CatmullRomSpline(t, Value1.Bottom, Value2.Bottom, Value3.Bottom, Value4.Bottom)
			);
		}
	}
}
